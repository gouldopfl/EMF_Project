using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAssessmentService
{
    private readonly IClaimIssueAdjudicationDetailsService _details;
    private readonly ClaimIssueAdjudicationReadinessService _readiness;

    public ClaimIssueAdjudicationAssessmentService(
        IClaimIssueAdjudicationDetailsService details,
        ClaimIssueAdjudicationReadinessService readiness)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(readiness);

        _details = details;
        _readiness = readiness;
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

        return new ClaimIssueAdjudicationAssessment
        {
            Details = details,
            Readiness = _readiness.Assess(details)
        };
    }
}
