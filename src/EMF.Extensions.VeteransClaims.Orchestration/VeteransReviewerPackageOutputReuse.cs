using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerPackageOutputReuseDecision(
    bool ReuseSealedPackage, EvidencePackageId PackageId, string CurrentFingerprint,
    string? SealedSnapshotSha256, string Reason);

/// <summary>Independent of intelligence reuse. Compares validated V1 renderer inputs,
/// not generated DOCX/PDF bytes. A caller must already have validated summary reuse.</summary>
public static class VeteransReviewerPackageOutputReuse
{
    public static VeteransReviewerPackageOutputReuseDecision Decide(
        ReviewerPackageSnapshotRead candidate, ReviewerPackageSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var fingerprint = Fingerprint(current);
        if (candidate.IsLegacy && candidate.Snapshot is not null)
            throw new InvalidDataException("Legacy package unexpectedly contains a snapshot.");
        if (candidate.Snapshot is null)
            return new(false, current.PackageId, fingerprint, null,
                candidate.IsLegacy ? "Legacy package has no immutable output contract." : "Pending package has no sealed output contract.");
        var storedFingerprint = Fingerprint(candidate.Snapshot);
        return fingerprint == storedFingerprint
            ? new(true, candidate.Snapshot.PackageId, fingerprint, candidate.Snapshot.Sha256, "Current renderer inputs match sealed V1 inputs.")
            : new(false, current.PackageId, fingerprint, candidate.Snapshot.Sha256, "Current renderer inputs differ from sealed V1 inputs.");
    }

    public static string Fingerprint(ReviewerPackageSnapshot snapshot)
    {
        // Full validation precedes projection: corrupt/unsupported rows are never
        // downgraded to an ordinary cache miss or used to rebuild historical state.
        _ = VeteransReviewerPackageSnapshot.Restore(snapshot);
        var root = JsonNode.Parse(snapshot.Payload)!;
        var package = root["Details"]!["PackageDetails"]!;
        package["Package"]!["Id"] = "output-reuse-identity";
        foreach (var member in package["Artifacts"]!.AsArray())
            member!["EvidencePackageId"] = "output-reuse-identity";
        foreach (var regulation in root["Regulations"]!.AsArray())
        {
            // The renderer prints the retrieval calendar date, but not the time.
            var retrieved = DateTimeOffset.Parse(regulation!["RetrievedUtc"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            regulation["RetrievedUtc"] = retrieved.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        using var document = JsonDocument.Parse(root.ToJsonString());
        return ReviewerPackageSnapshot.ComputeHash("reviewer-output-reuse-v1\n" +
            VeteransReviewerPackageSnapshot.Canonical(document.RootElement));
    }
}
