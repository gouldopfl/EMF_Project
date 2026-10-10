using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EMF.Core.Contracts.Zip;

// Correlation supplied by a trusted host. Reservation is not authentication or approval.
// Revision/profile/actor are deliberately excluded from the lookup key: a changed
// proposal for the same logical request must fail, rather than allocate a new operation.
public sealed record ZipAdmissionKey(string IssuerId, string RequestId);
public sealed record ZipAdmissionBinding(string WorkflowId, string WorkflowOperationId, string ActivityId,
    string ParentArtifactId, string SourceContentId, string SourceRevision, string OriginalActorId,
    string AuthorizedOperationId, string ProfileJson, string ProfileHash,
    string ParentNamespaceId, string ChildNamespaceId);

[Flags]
public enum ZipAuthorityCapabilities { None = 0, Admit = 1, Recover = 2, Review = 4, DelegateChildren = 8, Cleanup = 16 }

// Evidence from the future issuer. The journal records this; it never issues it.
// Even unexpired evidence is not a live execution grant. The authenticated host
// must revalidate current permission separately before starting payload work.
public sealed record ZipAdmissionApproval(string ParentOperationId, string BindingHash, string IssuerId,
    string DecisionId, string AuthorityRevision, string PolicyVersion, ZipAuthorityCapabilities Capabilities,
    DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc);
public enum ZipAdmissionState { ApprovalPending, Approved, SourceBound, ParentBound, Completed, Denied, Revoked, RequiresReview }
public enum ZipAdmissionEventKind { Approved, SourceBound, ParentBound, Completed, Denied, Revoked, RequiresReview, AuthorityValidated }
public sealed record ZipAdmissionEvent(string EventId, ZipAdmissionEventKind Kind, string ExecutingActorId,
    DateTimeOffset OccurredUtc, ZipAdmissionApproval? Approval = null, ZipRetainedBinding? Source = null,
    ZipParentBinding? Parent = null, string? SafeReason = null);
public sealed record ZipParentAdmission(string OperationId, ZipAdmissionKey Key, ZipAdmissionBinding Binding,
    DateTimeOffset CreatedUtc, long Revision = 0, ZipAdmissionState State = ZipAdmissionState.ApprovalPending,
    ZipAdmissionApproval? Approval = null, ZipRetainedBinding? Source = null, ZipParentBinding? Parent = null,
    string? SafeReason = null, ZipAdmissionApproval? LatestAuthorityEvidence = null);

public static class ZipAdmissionValidation
{
    public static void Text(string? value, int maximum = 128)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > maximum ||
            value.Any(char.IsControl)) throw new InvalidDataException("Invalid ZIP admission identity.");
    }
    public static void Hash(string? value)
    {
        if (value is not { Length: 64 } || value.Any(c => !char.IsAsciiHexDigit(c) || char.IsAsciiLetterLower(c)))
            throw new InvalidDataException("Invalid ZIP admission hash.");
    }
    public static void Binding(ZipAdmissionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        foreach (var value in new[] { binding.WorkflowId, binding.WorkflowOperationId, binding.ActivityId,
            binding.ParentArtifactId, binding.SourceContentId, binding.SourceRevision, binding.AuthorizedOperationId }) Text(value);
        Text(binding.OriginalActorId, 256);
        Hash(binding.ProfileHash); Hash(binding.ParentNamespaceId); Hash(binding.ChildNamespaceId);
        if (binding.ParentNamespaceId == binding.ChildNamespaceId)
            throw new InvalidDataException("ZIP parent and child namespaces must be distinct.");
        if (string.IsNullOrWhiteSpace(binding.ProfileJson) || Encoding.UTF8.GetByteCount(binding.ProfileJson) > 16_384)
            throw new InvalidDataException("Missing or oversized ZIP profile.");
        using var profile = JsonDocument.Parse(binding.ProfileJson, new JsonDocumentOptions { MaxDepth = 8 });
        if (profile.RootElement.ValueKind != JsonValueKind.Object ||
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding.ProfileJson))) != binding.ProfileHash)
            throw new InvalidDataException("ZIP profile fingerprint changed.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in profile.RootElement.EnumerateObject())
            if (!names.Add(field.Name)) throw new InvalidDataException("Duplicate ZIP profile field.");
        var root = profile.RootElement;
        if (!root.TryGetProperty("encoding", out var encoding) || encoding.ValueKind != JsonValueKind.String ||
            encoding.GetString() != "EMF.ZipDurableProfile.v1" ||
            !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 5 ||
            !root.TryGetProperty("parentNamespace", out var parent) || parent.ValueKind != JsonValueKind.String || parent.GetString() != binding.ParentNamespaceId ||
            !root.TryGetProperty("childNamespace", out var child) || child.ValueKind != JsonValueKind.String || child.GetString() != binding.ChildNamespaceId)
            throw new InvalidDataException("ZIP profile does not bind its admission namespaces/version.");
    }
    public static string BindingHash(ZipAdmissionBinding binding)
    {
        Binding(binding);
        // A fixed record shape, domain-separated from profile and plan fingerprints.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "EMF.ZipAdmissionBinding.v1\0" + JsonSerializer.Serialize(binding))));
    }
}
