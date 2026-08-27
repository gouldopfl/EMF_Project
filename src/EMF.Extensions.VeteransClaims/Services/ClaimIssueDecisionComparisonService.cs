using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionComparisonService
{
    public ClaimIssueDecisionComparison Compare(
        ClaimIssueDecisionRecommendation recommendation,
        IssueDecision issueDecision)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(issueDecision);

        if (recommendation.ClaimIssueId !=
            issueDecision.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Decision comparison claim issue mismatch.");
        }

        var outcome =
            recommendation.RecommendedOutcome is null
                ? ClaimIssueDecisionComparisonOutcomes
                    .NotComparable
                : recommendation.RecommendedOutcome ==
                    issueDecision.Outcome
                    ? ClaimIssueDecisionComparisonOutcomes
                        .Agreement
                    : ClaimIssueDecisionComparisonOutcomes
                        .Disagreement;

        return new ClaimIssueDecisionComparison
        {
            ClaimIssueId = recommendation.ClaimIssueId,
            IssueDecision = issueDecision,
            Recommendation = recommendation,
            ComparisonOutcome = outcome
        };
    }
}
