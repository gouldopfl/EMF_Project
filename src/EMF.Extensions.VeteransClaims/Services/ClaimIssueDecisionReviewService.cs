using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionReviewService
{
    public ClaimIssueDecisionReview Assess(
        ClaimIssueDecisionComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(comparison.IssueDecision);
        ArgumentNullException.ThrowIfNull(comparison.Recommendation);

        if (comparison.ClaimIssueId !=
            comparison.IssueDecision.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Decision review issue decision claim issue mismatch.");
        }

        if (comparison.ClaimIssueId !=
            comparison.Recommendation.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Decision review recommendation claim issue mismatch.");
        }

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
