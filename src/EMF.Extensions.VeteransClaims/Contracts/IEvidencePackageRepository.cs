using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidencePackageRepository
{
    Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<EvidencePackageArtifact>>
        GetEvidencePackageArtifactsAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Evidence package artifacts are not supported by this repository.");
    }
}
