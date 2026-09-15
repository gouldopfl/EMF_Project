using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClinicalProgressionRepository
{
    Task AddAsync(
        ClinicalProgressionEvent progressionEvent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicalProgressionEvent>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
