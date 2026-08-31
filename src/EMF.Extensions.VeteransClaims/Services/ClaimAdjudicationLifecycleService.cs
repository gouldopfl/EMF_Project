using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimAdjudicationLifecycleService
{
    private readonly IClaimIssueRepository _issues;
    private readonly ClaimIssueAdjudicationLifecycleService _lifecycle;

    public ClaimAdjudicationLifecycleService(
        IClaimIssueRepository issues,
        ClaimIssueAdjudicationLifecycleService lifecycle)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(lifecycle);

        _issues = issues;
        _lifecycle = lifecycle;
    }

    public async Task<IReadOnlyList<ClaimIssueAdjudicationLifecycleEntry>>
        GetAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        var issues =
            await _issues.GetClaimIssuesAsync(
                claimId,
                cancellationToken);

        var entries =
            new List<ClaimIssueAdjudicationLifecycleEntry>();

        foreach (var issue in issues)
        {
            entries.AddRange(
                await _lifecycle.GetAsync(
                    issue.Id,
                    cancellationToken));
        }

        return entries
            .OrderBy(x => x.VaDecision.DecisionDate)
            .ToArray();
    }
}
