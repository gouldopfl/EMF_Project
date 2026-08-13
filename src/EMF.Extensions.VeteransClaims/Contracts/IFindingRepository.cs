using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IFindingRepository
{
    Task AddFindingAsync(
        Finding finding,
        CancellationToken cancellationToken = default);

    Task<Finding?> GetFindingAsync(
        FindingId findingId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Finding>> GetFindingsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
