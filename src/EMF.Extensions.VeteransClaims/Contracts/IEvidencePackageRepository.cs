using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidencePackageRepository
{
    Task<ReviewerPackageSnapshotRead> ReadReviewerSnapshotAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer snapshot state reads are not supported.");

    // Null means a new, not-yet-sealed package. Legacy packages fail closed.
    Task<ReviewerPackageSnapshot?> GetReviewerSnapshotAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer snapshots are not supported.");

    Task SaveReviewerSnapshotAsync(ReviewerPackageSnapshot snapshot,
        EvidencePackageDetails expectedMembership, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer snapshots are not supported.");

    bool SupportsReviewerOutputProvenance => false;

    Task<IReadOnlyList<ReviewerPackageOutputProvenance>> GetReviewerOutputProvenanceAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer output provenance is not supported.");

    Task SaveReviewerOutputProvenanceAsync(ReviewerPackageOutputProvenance provenance,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer output provenance is not supported.");

    Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        CancellationToken cancellationToken = default);

    Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        IReadOnlyCollection<EvidencePackageArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Atomic evidence package persistence is not supported.");
    }

    Task<EvidencePackage?> GetEvidencePackageAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidencePackage>>
        GetEvidencePackagesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);


    Task AddEvidencePackageArtifactAsync(
        EvidencePackageArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Evidence package artifacts are not supported by this repository.");
    }

    Task SetReviewerPageSelectionAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string? reviewerPageSelection,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Reviewer page selection updates are not supported.");
    }

    Task<IReadOnlyList<EvidencePackageArtifact>>
        GetEvidencePackageArtifactsAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Evidence package artifacts are not supported by this repository.");
    }
}
