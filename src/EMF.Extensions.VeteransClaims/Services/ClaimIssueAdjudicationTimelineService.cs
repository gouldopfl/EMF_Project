using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationTimelineService
{
    public IReadOnlyList<ClaimIssueAdjudicationEvent> Compose(
        IReadOnlyCollection<ClaimIssueAdjudicationLifecycleEntry> vaEntries,
        IReadOnlyCollection<ClaimIssueAdjudicationEvent> otherEvents)
    {
        ArgumentNullException.ThrowIfNull(vaEntries);
        ArgumentNullException.ThrowIfNull(otherEvents);

        return vaEntries
            .Select(
                ClaimIssueAdjudicationEventFactory
                    .FromLifecycleEntry)
            .Concat(otherEvents)
            .OrderBy(x => x.OccurredAt)
            .ToArray();
    }
}
