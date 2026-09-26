using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerPackageReuseSelection(
    EvidencePackageId PackageId, bool ReusedSealedPackage,
    ReviewerPackageSnapshot? OutputSnapshot, string Reason, string? Fingerprint);

/// <summary>Called only after summary/intelligence reuse has independently succeeded.</summary>
public sealed class VeteransReviewerPackageReuseService(IEvidencePackageRepository packages)
{
    public async Task<VeteransReviewerPackageReuseSelection> SelectAsync(
        EvidencePackageId summaryOwner, EvidencePackageDetails requested,
        Func<EvidencePackageDetails, CancellationToken, Task<VeteransReviewerPackageDetails>> assembleCurrent,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider,
        bool outputRequested, CancellationToken cancellationToken = default)
    {
        var candidate = await packages.ReadReviewerSnapshotAsync(summaryOwner, cancellationToken);
        if (candidate.IsLegacy && candidate.Snapshot is not null)
            throw new InvalidDataException("Legacy package unexpectedly contains a snapshot.");
        if (candidate.Snapshot is not null)
        {
            if (candidate.Snapshot.PackageId != summaryOwner)
                throw new InvalidDataException("Reviewer output candidate identity mismatch.");
            _ = VeteransReviewerPackageSnapshot.Restore(candidate.Snapshot);
        }
        if (requested.Package.Id == summaryOwner)
            throw new InvalidDataException("Current-view package preparation requires a fresh identity.");

        ReviewerPackageSnapshot? current = null;
        VeteransReviewerPackageOutputReuseDecision? decision = null;
        if (outputRequested)
        {
            var details = await assembleCurrent(requested, cancellationToken);
            current = await new VeteransReviewerPackageDocumentOutputService(regulatoryTextProvider: regulatoryTextProvider)
                .CaptureCurrentAsync(details, cancellationToken);
            current.ValidateMembership(requested);
            decision = VeteransReviewerPackageOutputReuse.Decide(candidate, current);
            if (decision.ReuseSealedPackage)
                return new(decision.PackageId, true, candidate.Snapshot, decision.Reason, decision.CurrentFingerprint);
        }

        // Preserve the independently reusable summary artifact, but persist a fresh
        // package with the exact current requested members and inherited selections.
        await packages.AddEvidencePackageAsync(requested.Package, requested.Artifacts.ToArray(), cancellationToken);
        return new(requested.Package.Id, false, current,
            decision?.Reason ?? "Summary reused; no output comparison requested. Created a pending current-view package.",
            decision?.CurrentFingerprint);
    }
}
