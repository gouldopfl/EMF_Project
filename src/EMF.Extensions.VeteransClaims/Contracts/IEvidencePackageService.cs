using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidencePackageService
{
    Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        CancellationToken cancellationToken = default);

    Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        IReadOnlyCollection<ArtifactId>
            underlyingEvidenceArtifactIds,
        IReadOnlyCollection<ArtifactId>
            generatedOrganizationalMaterialArtifactIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Atomic evidence package creation is not supported.");

    Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        IReadOnlyCollection<ArtifactId>
            underlyingEvidenceArtifactIds,
        IReadOnlyCollection<ArtifactId>
            generatedOrganizationalMaterialArtifactIds,
        ServiceConnectionBasisId? serviceConnectionBasisId,
        CancellationToken cancellationToken = default) =>
        serviceConnectionBasisId is null
            ? CreateAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                underlyingEvidenceArtifactIds,
                generatedOrganizationalMaterialArtifactIds,
                cancellationToken)
            : throw new NotSupportedException(
                "Basis-scoped evidence package creation is not supported.");

    Task<EvidencePackageArtifact> AddArtifactAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string contentRole,
        CancellationToken cancellationToken = default);

    Task<EvidencePackageArtifact> SetReviewerPageSelectionAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string? reviewerPageSelection,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Reviewer page selection updates are not supported.");

    Task<EvidencePackageDetails?> GetAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidencePackageDetails>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
