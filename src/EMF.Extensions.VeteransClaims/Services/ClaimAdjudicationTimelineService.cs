using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimAdjudicationTimelineService :
    IClaimAdjudicationTimelineService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimIssueAdjudicationTimelineService _timeline;

    public ClaimAdjudicationTimelineService(
        IClaimIssueRepository issues,
        IClaimIssueAdjudicationTimelineService timeline)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(timeline);

        _issues = issues;
        _timeline = timeline;
    }

    public async Task<IReadOnlyList<ClaimIssueAdjudicationEvent>>
        GetAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        var issues =
            await _issues.GetClaimIssuesAsync(
                claimId,
                cancellationToken);

        var events =
            new List<ClaimIssueAdjudicationEvent>();

        foreach (var issue in issues)
        {
            events.AddRange(
                await _timeline.GetAsync(
                    issue.Id,
                    cancellationToken));
        }

        return events
            .OrderBy(x => x.OccurredAt)
            .ToArray();
    }
}
