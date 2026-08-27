using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionRecommendationService
{
    public ClaimIssueDecisionRecommendation Assess(
        ClaimIssueAdjudicationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var merits =
            assessment.Merits
            ?? throw new InvalidOperationException(
                "Claim issue merits assessment is required.");

        return new ClaimIssueDecisionRecommendation
        {
            ClaimIssueId = merits.ClaimIssueId,
            IsReadyForAdjudication =
                assessment.Readiness.IsReadyForAdjudication,
            MeritsOutcome = merits.Outcome,
            RecommendedOutcome =
                DeriveRecommendation(
                    assessment.Readiness.IsReadyForAdjudication,
                    merits.Outcome)
        };
    }

    private static string? DeriveRecommendation(
        bool isReady,
        string meritsOutcome)
    {
        if (!isReady)
            return null;

        return meritsOutcome switch
        {
            FindingOutcomes.Favorable =>
                IssueDecisionOutcomes.Granted,

            FindingOutcomes.PartiallyFavorable =>
                IssueDecisionOutcomes.PartiallyGranted,

            FindingOutcomes.Unfavorable =>
                IssueDecisionOutcomes.Denied,

            FindingOutcomes.Disputed => null,

            FindingOutcomes.Unresolved => null,

            _ => throw new InvalidOperationException(
                $"Unknown merits outcome '{meritsOutcome}'.")
        };
    }
}
