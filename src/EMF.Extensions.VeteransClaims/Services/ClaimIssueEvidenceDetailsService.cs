using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueEvidenceDetailsService :
    IClaimIssueEvidenceDetailsService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimIssueEvidenceChecklistService _checklist;
    private readonly IEvidenceDevelopmentPlanService _plans;

    public ClaimIssueEvidenceDetailsService(
        IClaimIssueRepository issues,
        IClaimIssueEvidenceChecklistService checklist,
        IEvidenceDevelopmentPlanService plans)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(checklist);
        ArgumentNullException.ThrowIfNull(plans);

        _issues = issues;
        _checklist = checklist;
        _plans = plans;
    }

    public async Task<ClaimIssueEvidenceDetails?>
        GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        var issue =
            await _issues.GetClaimIssueAsync(
                claimIssueId,
                cancellationToken);

        if (issue is null)
            return null;

        if (issue.Id != claimIssueId)
            throw new InvalidOperationException(
                "Claim issue lookup returned a different issue.");

        var checklist =
            await _checklist.CreateChecklistAsync(
                claimIssueId,
                cancellationToken);

        if (checklist.ClaimIssueId != claimIssueId)
            throw new InvalidOperationException(
                "Evidence checklist claim issue mismatch.");

        var plans =
            await _plans.GetEvidenceDevelopmentPlansAsync(
                claimIssueId,
                cancellationToken);

        if (plans.Any(x => x.ClaimIssueId != claimIssueId))
            throw new InvalidOperationException(
                "Evidence development plan claim issue mismatch.");

        return new ClaimIssueEvidenceDetails
        {
            ClaimIssue = issue,
            Checklist = checklist,
            DevelopmentPlans = plans
        };
    }
}
