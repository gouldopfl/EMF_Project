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

        var lastClosingIndex =
            Array.FindLastIndex(
                ordered,
                x =>
                    x.EventType ==
                        ClaimIssueAdjudicationEventTypes.VaDecision ||
                    x.EventType ==
                        ClaimIssueAdjudicationEventTypes.CourtDecision);

        var pendingEvents =
            ordered
                .Skip(lastClosingIndex + 1)
                .ToArray();

        if (pendingEvents.Length == 0)
        {
            throw new InvalidOperationException(
                "Adjudication cycle is closed.");
        }

        var pendingSince =
            pendingEvents[0].OccurredAt;

        var lastActivityAt =
            pendingEvents[^1].OccurredAt;

        return new ClaimIssueAdjudicationAging
        {
            ClaimIssueId = claimIssueId,
            PendingSince = pendingSince,
            AgeInDays =
                Math.Max(
                    0,
                    (int)(asOf.Date - pendingSince.Date).TotalDays),
            LastActivityAt = lastActivityAt,
            DaysSinceLastActivity =
                Math.Max(
                    0,
                    (int)(asOf.Date - lastActivityAt.Date).TotalDays)
        };
    }
}
