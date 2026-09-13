using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidencePackagePreparationService
{
    Task<EvidencePackage> PrepareAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        CancellationToken cancellationToken = default);

    Task<EvidencePackage> PrepareAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        IReadOnlyCollection<ArtifactId>
            generatedOrganizationalMaterialArtifactIds,
        CancellationToken cancellationToken = default);

    Task<EvidencePackage> PrepareAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        IReadOnlyCollection<ArtifactId>
            underlyingEvidenceArtifactIds,
        IReadOnlyCollection<ArtifactId>
            generatedOrganizationalMaterialArtifactIds,
        CancellationToken cancellationToken = default);

    Task<EvidencePackage> PrepareAsync(
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
            ? PrepareAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                underlyingEvidenceArtifactIds,
                generatedOrganizationalMaterialArtifactIds,
                cancellationToken)
            : throw new NotSupportedException(
                "Basis-scoped evidence package preparation is not supported.");

}
