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

    public ClaimIssueAdjudicationAssessmentService(
        IClaimIssueAdjudicationDetailsService details,
        ClaimIssueAdjudicationReadinessService readiness,
        ClaimIssueMeritsAssessmentService merits,
        ClaimIssueDecisionRecommendationService recommendations)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(merits);
        ArgumentNullException.ThrowIfNull(recommendations);

        _details = details;
        _readiness = readiness;
        _merits = merits;
        _recommendations = recommendations;
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

        var assessment =
            new ClaimIssueAdjudicationAssessment
            {
                Details = details,
                Readiness = _readiness.Assess(details),
                Merits = merits
            };

        return new ClaimIssueAdjudicationAssessment
        {
            Details = assessment.Details,
            Readiness = assessment.Readiness,
            Merits = assessment.Merits,
            Recommendation =
                _recommendations.Assess(assessment)
        };
    }
}
