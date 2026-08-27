using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAssessmentService
{
    private readonly IClaimIssueAdjudicationDetailsService _details;
    private readonly ClaimIssueAdjudicationReadinessService _readiness;
    private readonly ClaimIssueMeritsAssessmentService _merits;

    public ClaimIssueAdjudicationAssessmentService(
        IClaimIssueAdjudicationDetailsService details,
        ClaimIssueAdjudicationReadinessService readiness,
        ClaimIssueMeritsAssessmentService merits)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(merits);

        _details = details;
        _readiness = readiness;
        _merits = merits;
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

        return new ClaimIssueAdjudicationAssessment
        {
            Details = details,
            Readiness = _readiness.Assess(details),
            Merits = merits
        };
    }
}
