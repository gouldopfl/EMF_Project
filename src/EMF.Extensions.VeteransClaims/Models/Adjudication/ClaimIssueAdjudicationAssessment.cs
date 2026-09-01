namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationAssessment
{
    public required ClaimIssueAdjudicationDetails Details { get; init; }

    public required ClaimIssueAdjudicationReadiness Readiness { get; init; }

    public ClaimIssueAdjudicationAgingStatus? Aging { get; init; }

    public ClaimIssueMeritsOutcomeAssessment? Merits { get; init; }

    public ClaimIssueDecisionRecommendation? Recommendation { get; init; }

    public ClaimIssueCurrentDecision? CurrentDecision { get; init; }

    public IReadOnlyList<ClaimIssueDecisionReviewAnalysis> DecisionReviewHistory { get; init; } = [];

    public bool RequiresAttention =>
        !Readiness.IsReadyForAdjudication ||
        Aging?.RequiresAttention == true;

    public bool ShouldConsiderFollowUp =>
        Aging?.ShouldConsiderFollowUp == true;
}
