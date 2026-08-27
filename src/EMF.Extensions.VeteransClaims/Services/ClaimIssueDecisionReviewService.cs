using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionReviewService
{
    public ClaimIssueDecisionReview Assess(
        ClaimIssueDecisionComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        return new ClaimIssueDecisionReview
        {
            ClaimIssueId = comparison.ClaimIssueId,
            Comparison = comparison,
            RequiresReview =
                comparison.ComparisonOutcome ==
                    ClaimIssueDecisionComparisonOutcomes.Disagreement
        };
    }
}
