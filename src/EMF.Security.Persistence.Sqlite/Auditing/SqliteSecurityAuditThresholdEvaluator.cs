using EMF.Security.Monitoring;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SqliteSecurityAuditThresholdEvaluator
{
    private readonly SqliteSecurityAuditOperationReporter
        _reporter;
    private readonly ISecurityAlertSink _alertSink;

    public SqliteSecurityAuditThresholdEvaluator(
        string databasePath,
        ISecurityAlertSink alertSink)
    {
        ArgumentNullException.ThrowIfNull(alertSink);

        _reporter =
            new SqliteSecurityAuditOperationReporter(
                databasePath);

        _alertSink = alertSink;
    }

    public async Task<bool> EvaluateAsync(
        SecurityAuditThresholdRule rule,
        DateTimeOffset observedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            rule.AlertType);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            rule.Operation);

        if (rule.Threshold <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(rule.Threshold));

        if (rule.Window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(rule.Window));

        var windowStartedUtc =
            observedUtc - rule.Window;

        var report = await _reporter.CreateAsync(
            rule.Operation,
            windowStartedUtc,
            cancellationToken);

        report.OutcomeCounts.TryGetValue(
            rule.Outcome,
            out var eventCount);

        if (eventCount < rule.Threshold)
            return false;

        await _alertSink.WriteAsync(
            new SecurityAlert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                AlertType = rule.AlertType,
                Severity = rule.Severity,
                Operation = rule.Operation,
                ObservedUtc = observedUtc,
                EventCount = eventCount,
                WindowStartedUtc = windowStartedUtc,
                Facts = new Dictionary<string, string>
                {
                    ["outcome"] = rule.Outcome.ToString(),
                    ["threshold"] = rule.Threshold.ToString(),
                    ["chainHeadHash"] =
                        report.ChainHeadHash ?? ""
                }
            },
            cancellationToken);

        return true;
    }
}
