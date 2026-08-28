using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAgingService
{
    public ClaimIssueAdjudicationAging Assess(
        ClaimIssueId claimIssueId,
        IReadOnlyCollection<ClaimIssueAdjudicationEvent> timeline,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        if (timeline.Count == 0)
            throw new InvalidOperationException(
                "Cannot assess aging without timeline events.");

        var ordered =
            timeline
                .OrderBy(x => x.OccurredAt)
                .ToArray();

        var lastEvent = ordered[^1];

        if (lastEvent.EventType ==
            ClaimIssueAdjudicationEventTypes.VaDecision)
        {
            throw new InvalidOperationException(
                "Adjudication cycle is closed.");
        }

        var pendingSince = ordered[0].OccurredAt;
        var lastActivityAt = lastEvent.OccurredAt;

        return new ClaimIssueAdjudicationAging
        {
            ClaimIssueId = claimIssueId,
            PendingSince = pendingSince,
            AgeInDays =
                Math.Max(
                    0,
                    (int)(asOf.Date - pendingSince.Date).TotalDays),
            LastActivityAt = lastActivityAt
        };
    }
}
