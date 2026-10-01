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

    bool SupportsReviewerPresentationSnapshots => false;
    Task<ReviewerPackagePresentationSnapshot?> GetReviewerPresentationAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Prepared reviewer presentation is not supported.");
    // Creates the identity, membership, source and presentation in one transaction.
    // Implementations must fail without writing if atomic creation is unsupported.
    Task CreateReviewerPresentationVersionAsync(ReviewerPackageSnapshot snapshot,
        EvidencePackageDetails membership, ReviewerPackagePresentationSnapshot presentation,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Atomic reviewer presentation version creation is not supported.");

    // Explicit recovery of a caller-reviewed incomplete identity. Never changes
    // its existing membership/source or reconstructs an exported legacy package.
    Task RecoverReviewerPresentationVersionAsync(ReviewerPackageSnapshot snapshot,
        EvidencePackageDetails membership, ReviewerPackagePresentationSnapshot presentation,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Atomic reviewer presentation version recovery is not supported.");

    Task SaveReviewerPresentationAsync(ReviewerPackagePresentationSnapshot presentation,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Prepared reviewer presentation is not supported.");
    Task<ReviewerPackageFrozenPdf?> GetReviewerFrozenPdfAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Frozen reviewer PDF is not supported.");
    Task SaveReviewerFrozenPdfAsync(ReviewerPackageFrozenPdf pdf,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Frozen reviewer PDF is not supported.");

    bool SupportsReviewerOutputProvenance => false;

    Task<IReadOnlyList<ReviewerPackageOutputProvenance>> GetReviewerOutputProvenanceAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer output provenance is not supported.");

    Task SaveReviewerOutputProvenanceAsync(ReviewerPackageOutputProvenance provenance,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Reviewer output provenance is not supported.");


    bool SupportsReviewerOutputBuildProvenance => false;

    Task<IReadOnlyList<ReviewerPackageOutputBuildProvenance>>
        GetReviewerOutputBuildProvenanceAsync(
            string provenanceId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Reviewer output build provenance is not supported.");

    Task SaveReviewerOutputBuildProvenanceAsync(
        ReviewerPackageOutputBuildProvenance provenance,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Reviewer output build provenance is not supported.");


    Task SaveReviewerOutputWithBuildProvenanceAsync(
        ReviewerPackageOutputProvenance outputProvenance,
        ReviewerPackageOutputBuildProvenance buildProvenance,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Atomic reviewer output/build provenance is not supported.");

    Task SaveReviewerOutputWithBuildProvenanceAndManifestAsync(
        ReviewerPackageOutputProvenance outputProvenance,
        ReviewerPackageOutputBuildProvenance buildProvenance,
        string buildManifestJson,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Atomic reviewer output/build provenance with manifest archival is not supported.");

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
