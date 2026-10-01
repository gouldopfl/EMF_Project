namespace EMF.Security.Monitoring;

/// <summary>
/// Checks integrity independently of event thresholds. Hosts must inspect MonitoringUnavailable
/// results and their Failure context; delivery failures and cancellation propagate.
/// </summary>
public sealed class SecurityAuditMonitoringService
{
    private readonly ISecurityAuditMonitoringReader _reader;
    private readonly ISecurityAlertSink _sink;
    public SecurityAuditMonitoringService(ISecurityAuditMonitoringReader reader, ISecurityAlertSink sink)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sink);
        _reader = reader;
        _sink = sink;
    }

    public async Task<IReadOnlyList<SecurityTelemetryEvaluationResult>> EvaluateAsync(
        SecurityAuditIntegrityAlertPolicy integrityPolicy, IReadOnlyList<SecurityTelemetryExpectation> expectations,
        DateTimeOffset evaluationUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecurityMonitoringValidation.Validate(integrityPolicy);
        ArgumentNullException.ThrowIfNull(expectations);
        foreach (var expectation in expectations) SecurityMonitoringValidation.Validate(expectation);
        if (expectations.Select(e => e.ExpectationId).Distinct(StringComparer.Ordinal).Count() != expectations.Count)
            throw new ArgumentException("Expectation identities must be unique.", nameof(expectations));
        var evaluator = new SecurityTelemetryExpectationEvaluator();
        var preliminary = expectations.Select(e => evaluator.Evaluate(e, [], evaluationUtc)).ToArray();
        var active = expectations.Where((e, i) => !IsInactive(preliminary[i].Status)).ToArray();
        SecurityAuditMonitoringSnapshot snapshot;
        try
        {
            snapshot = await _reader.ReadAsync(active, evaluationUtc, cancellationToken);
        }
        catch (SecurityAuditMonitoringUnavailableException ex)
        {
            // Even with zero expectations an unavailable integrity monitor must remain observable.
            if (active.Length == 0) throw;
            return preliminary.Select(r => IsInactive(r.Status) ? r :
                r with { Status = SecurityTelemetryEvaluationStatus.MonitoringUnavailable, DeadlineUtc = null, WindowStartedUtc = null, Failure = ex }).ToArray();
        }
        if (snapshot.SourceId != integrityPolicy.AuditSourceId)
            throw new ArgumentException("Integrity policy does not match the monitoring reader source.", nameof(integrityPolicy));
        if (!snapshot.Integrity.IsValid)
        {
            var alert = new SecurityAuditIntegrityAlertDetector().Build(snapshot.Integrity, integrityPolicy, evaluationUtc)!;
            await Deliver(alert, snapshot.Integrity, cancellationToken);
            return preliminary.Select((r, i) => IsInactive(r.Status) ? r :
                expectations[i].SourceId != snapshot.SourceId ? SourceBindingFailure(r) :
                r with { Status = SecurityTelemetryEvaluationStatus.EvidenceUntrusted, DeadlineUtc = null, WindowStartedUtc = null }).ToArray();
        }
        var results = new List<SecurityTelemetryEvaluationResult>();
        foreach (var expectation in expectations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = evaluator.Evaluate(expectation, snapshot.Observations, evaluationUtc);
            if (!IsInactive(result.Status) && expectation.SourceId != snapshot.SourceId)
                result = SourceBindingFailure(result);
            results.Add(result);
            if (result.Status != SecurityTelemetryEvaluationStatus.Missing) continue;
            var facts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ExpectationId"] = expectation.ExpectationId,
                ["SourceId"] = expectation.SourceId,
                ["Operation"] = expectation.Operation,
                ["EvaluationUtc"] = SecurityMonitoringValidation.FormatUtc(evaluationUtc),
                ["DeadlineUtc"] = SecurityMonitoringValidation.FormatUtc(result.DeadlineUtc!.Value)
            };
            if (result.LastObservedUtc is { } last) facts.Add("LastObservedUtc", SecurityMonitoringValidation.FormatUtc(last));
            await Deliver(new SecurityAlert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                AlertType = "telemetry.required-missing",
                Operation = "security.telemetry.evaluate",
                Severity = expectation.Severity,
                ObservedUtc = evaluationUtc.ToUniversalTime(),
                WindowStartedUtc = result.WindowStartedUtc!.Value.ToUniversalTime(),
                EventCount = 0,
                Facts = facts
            }, null, cancellationToken);
        }
        return results;
    }

    private static SecurityTelemetryEvaluationResult SourceBindingFailure(SecurityTelemetryEvaluationResult result) =>
        result with
        {
            Status = SecurityTelemetryEvaluationStatus.MonitoringUnavailable,
            DeadlineUtc = null,
            WindowStartedUtc = null,
            Failure = new InvalidOperationException("Expectation source has no matching monitoring provider binding.")
        };

    private static bool IsInactive(SecurityTelemetryEvaluationStatus status) =>
        status is SecurityTelemetryEvaluationStatus.Disabled or
            SecurityTelemetryEvaluationStatus.NotExpected or SecurityTelemetryEvaluationStatus.Suspended;

    private async Task Deliver(SecurityAlert alert, SecurityAuditIntegrityDiagnostics? integrity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _sink.WriteAsync(alert, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecurityAlertDeliveryException(alert, integrity, ex);
        }
    }
}
