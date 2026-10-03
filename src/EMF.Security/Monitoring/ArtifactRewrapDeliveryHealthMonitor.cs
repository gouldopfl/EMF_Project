using EMF.Security.Storage;
namespace EMF.Security.Monitoring;

public sealed record ArtifactRewrapDeliveryHealth(int PendingSampleCount, int ReviewSampleCount,
    DateTimeOffset? OldestPendingUtc, bool SampleLimitReached);

// Bounded operational health, delivered outside the canonical audit chain.
// Deployment supplies scheduling, alert thresholds and the approved alert sink.
public sealed class ArtifactRewrapDeliveryHealthMonitor
{
    private readonly IArtifactRewrapJournal _journal;
    private readonly ISecurityAlertSink _alerts;
    public ArtifactRewrapDeliveryHealthMonitor(IArtifactRewrapJournal journal, ISecurityAlertSink alerts)
    { _journal = journal ?? throw new ArgumentNullException(nameof(journal)); _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts)); }
    public async Task<ArtifactRewrapDeliveryHealth> ObserveAsync(DateTimeOffset now, TimeSpan maximumPendingAge,
        int sampleLimit = 100, CancellationToken cancellationToken = default)
    {
        if (maximumPendingAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumPendingAge));
        if (sampleLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(sampleLimit));
        var pending = await _journal.ReadPendingAsync(sampleLimit, cancellationToken);
        var review = await _journal.ReadRecoveryWorkAsync(sampleLimit, cancellationToken);
        var oldest = pending.Count == 0 ? (DateTimeOffset?)null : pending.Min(x => x.Receipt?.OccurredUtc ?? x.AuthorizedUtc);
        var health = new ArtifactRewrapDeliveryHealth(pending.Count, review.Count, oldest, pending.Count == sampleLimit || review.Count == sampleLimit);
        if (review.Count != 0 || oldest is not null && now - oldest >= maximumPendingAge)
        {
            await _alerts.WriteAsync(new()
            {
                AlertId = "artifact-rewrap-delivery",
                AlertType = "SecurityMutationAuditDelivery",
                Severity = SecurityAlertSeverity.High,
                Operation = "artifact.envelope.rewrap",
                ObservedUtc = now,
                WindowStartedUtc = oldest ?? now,
                EventCount = pending.Count + review.Count,
                Facts = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["pendingSampleCount"] = pending.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["reviewSampleCount"] = review.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["sampleLimitReached"] = health.SampleLimitReached ? "true" : "false"
                }
            }, cancellationToken);
        }
        return health;
    }
}
