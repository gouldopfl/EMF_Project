using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimIssueAdjudicationAssessmentService
{
    Task<ClaimIssueAdjudicationAssessment?> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default);
}
