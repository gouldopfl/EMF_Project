using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimEvidenceDetailsService :
    IClaimEvidenceDetailsService
{
    private readonly IClaimRepository _claims;
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimIssueEvidenceDetailsService _evidence;

    public ClaimEvidenceDetailsService(
        IClaimRepository claims,
        IClaimIssueRepository issues,
        IClaimIssueEvidenceDetailsService evidence)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(evidence);

        _claims = claims;
        _issues = issues;
        _evidence = evidence;
    }

    public async Task<ClaimEvidenceDetails?>
        GetAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        var claim =
            await _claims.GetClaimAsync(
                claimId,
                cancellationToken);

        if (claim is null)
            return null;

        if (claim.Id != claimId)
            throw new InvalidOperationException(
                "Claim lookup returned a different claim.");

        var issues =
            await _issues.GetClaimIssuesAsync(
                claimId,
                cancellationToken);

        if (issues.Any(
            x => x.ClaimId != claimId))
        {
            throw new InvalidOperationException(
                "Claim lookup returned an issue for a different claim.");
        }

        var details =
            new List<ClaimIssueEvidenceDetails>();

        foreach (var issue in issues)
        {
            var evidence =
                await _evidence.GetAsync(
                    issue.Id,
                    cancellationToken);

            if (evidence is null)
                continue;

            if (evidence.ClaimIssue.Id != issue.Id)
            {
                throw new InvalidOperationException(
                    "Claim issue evidence identity mismatch.");
            }

            if (evidence.ClaimIssue.ClaimId != claimId)
            {
                throw new InvalidOperationException(
                    "Claim issue evidence claim ownership mismatch.");
            }

            details.Add(evidence);
        }

        return new ClaimEvidenceDetails
        {
            Claim = claim,
            Issues = details
        };
    }
}
