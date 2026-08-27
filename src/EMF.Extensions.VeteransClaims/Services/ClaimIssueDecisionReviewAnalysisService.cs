using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionReviewAnalysisService
{
    public ClaimIssueDecisionReviewAnalysis Analyze(
        ClaimIssueDecisionReview review,
        ClaimIssueMeritsOutcomeAssessment merits)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(merits);

        if (review.ClaimIssueId != merits.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Decision review analysis claim issue mismatch.");
        }

        var contributing =
            review.RequiresReview
                ? merits.TheoryOutcomes
                    .Where(x => x.Outcome == merits.Outcome)
                    .ToArray()
                : [];

        return new ClaimIssueDecisionReviewAnalysis
        {
            ClaimIssueId = merits.ClaimIssueId,
            Review = review,
            Merits = merits,
            ContributingTheoryOutcomes = contributing
        };
    }
}
