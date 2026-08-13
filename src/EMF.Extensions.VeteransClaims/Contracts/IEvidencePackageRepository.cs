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
}
