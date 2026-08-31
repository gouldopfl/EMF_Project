using EMF.Extensions.VeteransClaims.Models.Claims;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimAdjudicationAssessment
{
    public required Claim Claim { get; init; }

    public required IReadOnlyList<ClaimIssueAdjudicationAssessment>
        Issues { get; init; }

    public int IssueCount =>
        Issues.Count;

    public int ReadyIssueCount =>
        Issues.Count(x => x.Readiness.IsReadyForAdjudication);

    public int BlockedIssueCount =>
        Issues.Count(x => !x.Readiness.IsReadyForAdjudication);

    public int RecommendedIssueCount =>
        Issues.Count(
            x => x.Recommendation?.HasRecommendation == true);

    public int ReviewedDecisionCount =>
        Issues.Sum(x => x.DecisionReviewHistory.Count);

    public int ReviewRequiredCount =>
        Issues.Sum(
            x => x.DecisionReviewHistory.Count(
                review => review.Review.RequiresReview));

    public int AttentionIssueCount =>
        Issues.Count(x => x.RequiresAttention);

    public int FollowUpIssueCount =>
        Issues.Count(x => x.ShouldConsiderFollowUp);

    public bool RequiresAttention =>
        Issues.Any(x => x.RequiresAttention);

    public bool ShouldConsiderFollowUp =>
        Issues.Any(x => x.ShouldConsiderFollowUp);
}
