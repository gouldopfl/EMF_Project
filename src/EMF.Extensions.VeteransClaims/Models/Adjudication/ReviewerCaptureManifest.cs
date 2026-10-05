using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMF.Core.Contracts.Storage;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public readonly record struct OperationSnapshotId
{
    public string Value { get; }
    [JsonConstructor] public OperationSnapshotId(string value) => Value = ArtifactContentIdentity.Validate(value);
    public static OperationSnapshotId New() => new(Guid.NewGuid().ToString("N"));
}

// This proof profile captures explicitly selected adopted UTF-8 artifacts, not the
// entire reviewer query closure. It cannot be used to authorize reviewer assembly.
public sealed record ReviewerCapturedMetadata(string ArtifactId, string Representation, string Sha256,
    string? SourceRevision = null);
public sealed record ReviewerCapturedMember(string ArtifactId, string PhysicalRevision, long Length,
    string Sha256, string FingerprintAlgorithm, string Fingerprint, string ClassificationId,
    string ClassificationRevision);
public sealed record ReviewerCaptureManifest(int Version, OperationSnapshotId SnapshotId, string Profile,
    string[] RequiredMembers, string[] ExplicitAbsences, ReviewerCapturedMetadata[] Metadata,
    ReviewerCapturedMember[] Members);
public sealed record ReviewerCapturedBundle(ReviewerCaptureManifest Manifest, Dictionary<string, byte[]> Content);
// Trusted immutable receipt binds the exact retained representation, including revisions.
public sealed record ReviewerRetainedReference(OperationSnapshotId SnapshotId, string BundleSha256);
public sealed record ReviewerCaptureLimits(TimeSpan Deadline, int MaximumMembers = 32,
    long MaximumSourceBytes = 8 * 1024 * 1024, long MaximumMetadataBytes = 1024 * 1024,
    int MaximumWork = 4096)
{
    public void Validate()
    {
        if (Deadline <= TimeSpan.Zero || Deadline > TimeSpan.FromMinutes(5) || MaximumMembers <= 0 ||
            MaximumSourceBytes <= 0 || MaximumMetadataBytes <= 0 || MaximumWork <= 0)
            throw new ArgumentOutOfRangeException(nameof(Deadline));
    }
}
public static class ReviewerRetainedValidator
{
    public const string Profile = "Reviewer.AdoptedUtf8.ContractProof.v1";
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] Encode(ReviewerCapturedBundle bundle) => JsonSerializer.SerializeToUtf8Bytes(bundle);
    public static void Validate(ReviewerCapturedBundle bundle, OperationSnapshotId expected, CancellationToken ct = default)
    {
        try { ValidateCore(bundle, expected, ct); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw new InvalidDataException("Invalid retained manifest or metadata representation.", ex); }
    }
    private static void ValidateCore(ReviewerCapturedBundle bundle, OperationSnapshotId expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var m = bundle.Manifest ?? throw new InvalidDataException("Missing retained manifest.");
        if (m.Version != 1 || m.Profile != Profile || m.SnapshotId != expected ||
            m.RequiredMembers is null || m.Members is null || m.Metadata is null || m.ExplicitAbsences is null ||
            bundle.Content is null || m.RequiredMembers.Length == 0 ||
            m.Members.Any(x => x is null) || m.Metadata.Any(x => x is null) ||
            m.RequiredMembers.Distinct(StringComparer.Ordinal).Count() != m.RequiredMembers.Length ||
            !m.RequiredMembers.SequenceEqual(m.Members.Select(x => x.ArtifactId)) ||
            !m.RequiredMembers.SequenceEqual(m.Metadata.Select(x => x.ArtifactId)) ||
            !m.RequiredMembers.Order(StringComparer.Ordinal).SequenceEqual(bundle.Content.Keys.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Retained closure, identity or version mismatch.");
        ArtifactContentIdentity.Validate(m.SnapshotId.Value);
        var expectedAbsences = new List<string>();
        foreach (var member in m.Members)
        {
            ct.ThrowIfCancellationRequested();
            ArtifactContentIdentity.Validate(member.PhysicalRevision);
            ArtifactContentIdentity.Validate(member.ClassificationRevision);
            var bytes = bundle.Content[member.ArtifactId];
            var metadata = m.Metadata.Single(x => x.ArtifactId == member.ArtifactId);
            if (bytes is null || bytes.LongLength != member.Length || Hash(bytes) != member.Sha256 ||
                member.FingerprintAlgorithm != "SHA-256" || member.Fingerprint != member.Sha256 ||
                string.IsNullOrWhiteSpace(member.ClassificationId) || metadata.SourceRevision is not null ||
                Hash(System.Text.Encoding.UTF8.GetBytes(metadata.Representation)) != metadata.Sha256)
                throw new InvalidDataException("Retained input integrity mismatch.");
            using var representation = JsonDocument.Parse(metadata.Representation);
            var root = representation.RootElement;
            using var artifact = JsonDocument.Parse(root.GetProperty("Artifact").GetString()!);
            using var authority = JsonDocument.Parse(root.GetProperty("Authority").GetString()!);
            using var provenance = JsonDocument.Parse(root.GetProperty("Provenance").GetString()!);
            using var relationships = JsonDocument.Parse(root.GetProperty("Relationships").GetString()!);
            if (artifact.RootElement.GetArrayLength() != 1 || authority.RootElement.GetArrayLength() != 1)
                throw new InvalidDataException("Retained metadata cardinality mismatch.");
            var a = artifact.RootElement[0]; var auth = authority.RootElement[0];
            if (a.GetProperty("Id").GetString() != member.ArtifactId ||
                a.GetProperty("ArtifactType").GetString() != "text/plain" ||
                a.GetProperty("FingerprintAlgorithm").GetString() != member.FingerprintAlgorithm ||
                a.GetProperty("FingerprintValue").GetString() != member.Fingerprint ||
                auth.GetProperty("IsAdopted").GetInt64() != 1 ||
                auth.GetProperty("ClassificationId").GetString() != member.ClassificationId ||
                auth.GetProperty("ClassificationRevision").GetString() != member.ClassificationRevision)
                throw new InvalidDataException("Retained metadata binding mismatch.");
            foreach (var p in provenance.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (p.GetProperty("ArtifactId").GetString() != member.ArtifactId)
                    throw new InvalidDataException("Retained provenance binding mismatch.");
            }
            foreach (var r in relationships.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var source = r.GetProperty("SourceArtifactId").GetString();
                var target = r.GetProperty("TargetArtifactId").GetString();
                if ((source != member.ArtifactId && target != member.ArtifactId) ||
                    !m.RequiredMembers.Contains(source) || !m.RequiredMembers.Contains(target))
                    throw new InvalidDataException("Retained relationship escapes admitted closure.");
            }
            if (provenance.RootElement.GetArrayLength() == 0) expectedAbsences.Add(member.ArtifactId + ":provenance");
            if (relationships.RootElement.GetArrayLength() == 0) expectedAbsences.Add(member.ArtifactId + ":relationships");
            try { _ = new System.Text.UTF8Encoding(false, true).GetCharCount(bytes); }
            catch (System.Text.DecoderFallbackException ex) { throw new InvalidDataException("Unsupported source encoding.", ex); }
        }
        if (!m.ExplicitAbsences.SequenceEqual(expectedAbsences))
            throw new InvalidDataException("Retained explicit absences mismatch.");
        ct.ThrowIfCancellationRequested();
    }
}
