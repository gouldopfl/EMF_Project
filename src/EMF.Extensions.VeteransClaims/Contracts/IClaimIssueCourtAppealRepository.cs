using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimIssueCourtAppealRepository
{
    Task AddAsync(
        ClaimIssueCourtAppeal appeal,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimIssueCourtAppeal>>
        GetByClaimIssueAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
}
