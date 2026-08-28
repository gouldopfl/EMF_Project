using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAssessmentService
{
    private readonly IClaimIssueAdjudicationDetailsService _details;
    private readonly ClaimIssueAdjudicationReadinessService _readiness;
    private readonly ClaimIssueMeritsAssessmentService _merits;
    private readonly ClaimIssueDecisionRecommendationService _recommendations;
    private readonly ClaimIssueAdjudicationAgingStatusService _aging;
    private readonly ClaimIssueAdjudicationAgingPolicy _agingPolicy;

    public ClaimIssueAdjudicationAssessmentService(
        IClaimIssueAdjudicationDetailsService details,
        ClaimIssueAdjudicationReadinessService readiness,
        ClaimIssueMeritsAssessmentService merits,
        ClaimIssueDecisionRecommendationService recommendations,
        ClaimIssueAdjudicationAgingStatusService aging,
        ClaimIssueAdjudicationAgingPolicy agingPolicy)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(merits);
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(aging);
        ArgumentNullException.ThrowIfNull(agingPolicy);

        _details = details;
        _readiness = readiness;
        _merits = merits;
        _recommendations = recommendations;
        _aging = aging;
        _agingPolicy = agingPolicy;
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

        ClaimIssueAdjudicationAgingStatus? aging = null;

        if (details.Timeline.Count > 0)
        {
            var lastEvent =
                details.Timeline
                    .OrderBy(x => x.OccurredAt)
                    .Last();

            if (lastEvent.EventType !=
                    ClaimIssueAdjudicationEventTypes.VaDecision &&
                lastEvent.EventType !=
                    ClaimIssueAdjudicationEventTypes.CourtDecision)
            {
                aging =
                    _aging.Assess(
                        claimIssueId,
                        details.Timeline,
                        DateTimeOffset.UtcNow,
                        _agingPolicy);
            }
        }

        var assessment =
            new ClaimIssueAdjudicationAssessment
            {
                Details = details,
                Readiness = _readiness.Assess(details),
                Aging = aging,
                Merits = merits
            };

        return new ClaimIssueAdjudicationAssessment
        {
            Details = assessment.Details,
            Readiness = assessment.Readiness,
            Aging = assessment.Aging,
            Merits = assessment.Merits,
            Recommendation =
                _recommendations.Assess(assessment)
        };
    }
}
