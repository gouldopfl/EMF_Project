using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimAdjudicationAssessmentService
{
    private readonly IClaimRepository _claims;
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimIssueAdjudicationAssessmentService _assessments;

    public ClaimAdjudicationAssessmentService(
        IClaimRepository claims,
        IClaimIssueRepository issues,
        IClaimIssueAdjudicationAssessmentService assessments)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(assessments);

        _claims = claims;
        _issues = issues;
        _assessments = assessments;
    }

    public async Task<ClaimAdjudicationAssessment?> GetAsync(
        ClaimId claimId,
        CancellationToken cancellationToken = default)
    {
        var claim = await _claims.GetClaimAsync(claimId, cancellationToken);

        if (claim is null)
            return null;

        if (claim.Id != claimId)
            throw new InvalidOperationException(
                "Claim lookup returned a different claim.");

        var issues =
            await _issues.GetClaimIssuesAsync(claimId, cancellationToken);

        if (issues.Any(x => x.ClaimId != claimId))
            throw new InvalidOperationException(
                "Claim lookup returned an issue for a different claim.");

        var assessments =
            new List<ClaimIssueAdjudicationAssessment>();

        foreach (var issue in issues)
        {
            var assessment =
                await _assessments.GetAsync(issue.Id, cancellationToken);

            if (assessment is null)
            {
                throw new InvalidOperationException(
                    "Claim issue adjudication assessment could not be read.");
            }

            if (assessment.Details.ClaimIssue.Id != issue.Id)
                throw new InvalidOperationException(
                    "Claim issue assessment identity mismatch.");

            if (assessment.Details.ClaimIssue.ClaimId != claimId)
                throw new InvalidOperationException(
                    "Claim issue assessment claim ownership mismatch.");

            assessments.Add(assessment);
        }

        return new ClaimAdjudicationAssessment
        {
            Claim = claim,
            Issues = assessments
        };
    }
}
