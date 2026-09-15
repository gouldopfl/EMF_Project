using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface ISourceClarificationRepository
{
    Task AddAsync(
        SourceClarification clarification,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceClarification>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
