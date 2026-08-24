using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimIssueEvidenceChecklistService
{
    Task<ClaimIssueEvidenceChecklist>
        CreateChecklistAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
}
