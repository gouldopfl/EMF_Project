using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimIssueRepository
{
    Task AddClaimIssueAsync(
        ClaimIssue claimIssue,
        CancellationToken cancellationToken = default);

    Task<ClaimIssue?> GetClaimIssueAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimIssue>> GetClaimIssuesAsync(
        ClaimId claimId,
        CancellationToken cancellationToken = default);
}
