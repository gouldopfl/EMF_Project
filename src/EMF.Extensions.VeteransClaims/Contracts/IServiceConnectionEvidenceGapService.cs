using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IServiceConnectionEvidenceGapService
{
    Task<IReadOnlyList<EvidenceGap>> EnsureGapsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
