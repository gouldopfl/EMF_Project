using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimIssueAdjudicationDetailsService
{
    Task<ClaimIssueAdjudicationDetails?>
        GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
}
