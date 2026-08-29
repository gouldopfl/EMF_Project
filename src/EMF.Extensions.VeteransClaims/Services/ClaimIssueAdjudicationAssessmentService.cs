using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAssessmentService :
    IClaimIssueAdjudicationAssessmentService
{
    private readonly IClaimIssueAdjudicationDetailsService _details;
    private readonly ClaimIssueAdjudicationReadinessService _readiness;
    private readonly ClaimIssueMeritsAssessmentService _merits;
    private readonly ClaimIssueDecisionRecommendationService _recommendations;
    private readonly ClaimIssueDecisionReviewHistoryService _reviewHistory;
    private readonly ClaimIssueAdjudicationAgingStatusService _aging;
    private readonly ClaimIssueAdjudicationAgingPolicy _agingPolicy;
    private readonly TimeProvider _timeProvider;

    public ClaimIssueAdjudicationAssessmentService(
        IClaimIssueAdjudicationDetailsService details,
        ClaimIssueAdjudicationReadinessService readiness,
        ClaimIssueMeritsAssessmentService merits,
        ClaimIssueDecisionRecommendationService recommendations,
        ClaimIssueDecisionReviewHistoryService reviewHistory,
        ClaimIssueAdjudicationAgingStatusService aging,
        ClaimIssueAdjudicationAgingPolicy agingPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(merits);
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(reviewHistory);
        ArgumentNullException.ThrowIfNull(aging);
        ArgumentNullException.ThrowIfNull(agingPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _details = details;
        _readiness = readiness;
        _merits = merits;
        _recommendations = recommendations;
        _reviewHistory = reviewHistory;
        _aging = aging;
        _agingPolicy = agingPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<ClaimIssueAdjudicationAssessment?> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        var details =
            await _details.GetAsync(
                claimIssueId,
                cancellationToken);

        if (details is null)
            return null;

        var merits =
            await _merits.AssessAsync(
                claimIssueId,
                cancellationToken);

        var aging =
            _aging.TryAssess(
                claimIssueId,
                details.Timeline,
                _timeProvider.GetUtcNow(),
                _agingPolicy);

        var assessment =
            new ClaimIssueAdjudicationAssessment
            {
                Details = details,
                Readiness = _readiness.Assess(details),
                Aging = aging,
                Merits = merits
            };

        var recommendation =
            _recommendations.Assess(assessment);

        var reviewHistory =
            await _reviewHistory.GetAsync(
                recommendation,
                merits,
                cancellationToken);

        return new ClaimIssueAdjudicationAssessment
        {
            Details = assessment.Details,
            Readiness = assessment.Readiness,
            Aging = assessment.Aging,
            Merits = assessment.Merits,
            Recommendation = recommendation,
            DecisionReviewHistory = reviewHistory
        };
    }
}
