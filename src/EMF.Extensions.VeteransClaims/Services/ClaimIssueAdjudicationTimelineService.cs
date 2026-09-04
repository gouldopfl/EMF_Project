using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationTimelineService :
    IClaimIssueAdjudicationTimelineService
{
    private readonly ClaimIssueAdjudicationLifecycleService? _lifecycle;
    private readonly IClaimIssueCourtAppealRepository? _courtAppeals;

    public ClaimIssueAdjudicationTimelineService()
    {
    }

    public ClaimIssueAdjudicationTimelineService(
        ClaimIssueAdjudicationLifecycleService lifecycle,
        IClaimIssueCourtAppealRepository courtAppeals)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(courtAppeals);

        _lifecycle = lifecycle;
        _courtAppeals = courtAppeals;
    }

    public async Task<IReadOnlyList<ClaimIssueAdjudicationEvent>>
        GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        if (_lifecycle is null || _courtAppeals is null)
            throw new InvalidOperationException(
                "Timeline retrieval is not configured.");

        var vaEntries =
            await _lifecycle.GetAsync(
                claimIssueId,
                cancellationToken);

        var appeals =
            await _courtAppeals.GetByClaimIssueAsync(
                claimIssueId,
                cancellationToken);

        if (appeals.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Court appeal claim issue mismatch.");
        }

        var courtEvents =
            new List<ClaimIssueAdjudicationEvent>();

        foreach (var appeal in appeals)
        {
            courtEvents.Add(
                ClaimIssueAdjudicationEventFactory
                    .FromCourtAppeal(appeal));

            if (appeal.DecidedAt is null)
                continue;

            courtEvents.Add(
                string.Equals(
                    appeal.Outcome,
                    "Remanded",
                    StringComparison.OrdinalIgnoreCase)
                    ? ClaimIssueAdjudicationEventFactory
                        .FromRemand(appeal)
                    : ClaimIssueAdjudicationEventFactory
                        .FromCourtDecision(appeal));
        }

        return Compose(vaEntries, courtEvents);
    }

    public IReadOnlyList<ClaimIssueAdjudicationEvent> Compose(
        IReadOnlyCollection<ClaimIssueAdjudicationLifecycleEntry> vaEntries,
        IReadOnlyCollection<ClaimIssueAdjudicationEvent> otherEvents)
    {
        ArgumentNullException.ThrowIfNull(vaEntries);
        ArgumentNullException.ThrowIfNull(otherEvents);

        var issueIds =
            vaEntries
                .Select(x => x.ClaimIssueId)
                .Concat(otherEvents.Select(x => x.ClaimIssueId))
                .Distinct()
                .Take(2)
                .ToArray();

        if (issueIds.Length > 1)
        {
            throw new InvalidOperationException(
                "Timeline claim issue mismatch.");
        }

        var vaEvents =
            new List<ClaimIssueAdjudicationEvent>();

        foreach (var entry in vaEntries)
        {
            if (entry.Submission.SubmittedAt is not null)
            {
                vaEvents.Add(
                    ClaimIssueAdjudicationEventFactory
                        .FromSubmitted(entry));
            }

            if (entry.Submission.ReceivedAt is not null)
            {
                vaEvents.Add(
                    ClaimIssueAdjudicationEventFactory
                        .FromReceived(entry));
            }

            vaEvents.Add(
                ClaimIssueAdjudicationEventFactory
                    .FromLifecycleEntry(entry));
        }

        return vaEvents
            .Concat(otherEvents)
            .OrderBy(x => x.OccurredAt)
            .ToArray();
    }
}
