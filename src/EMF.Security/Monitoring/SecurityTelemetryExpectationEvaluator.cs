namespace EMF.Security.Monitoring;

/// <summary>Pure evaluation of trusted observation projections; owns no activation or suppression state.</summary>
public sealed class SecurityTelemetryExpectationEvaluator
{
    public SecurityTelemetryEvaluationResult Evaluate(SecurityTelemetryExpectation expectation,
        IReadOnlyList<SecurityTelemetryObservation> observations, DateTimeOffset evaluationUtc)
    {
        SecurityMonitoringValidation.Validate(expectation);
        ArgumentNullException.ThrowIfNull(observations);
        SecurityTelemetryEvaluationResult Status(SecurityTelemetryEvaluationStatus status) => new() { ExpectationId = expectation.ExpectationId, Status = status };
        if (!expectation.Enabled) return Status(SecurityTelemetryEvaluationStatus.Disabled);
        if (evaluationUtc < expectation.EffectiveFromUtc) return Status(SecurityTelemetryEvaluationStatus.NotExpected);
        var baseline = expectation.EffectiveFromUtc;
        foreach (var interval in expectation.MaintenanceIntervals)
        {
            if (evaluationUtc >= interval.StartedUtc && evaluationUtc < interval.EndedUtc)
                return Status(SecurityTelemetryEvaluationStatus.Suspended);
            if (interval.EndedUtc <= evaluationUtc && interval.EndedUtc > baseline) baseline = interval.EndedUtc;
        }
        var last = observations.Where(o => o.SourceId == expectation.SourceId && o.Operation == expectation.Operation &&
            (expectation.ResourceType is null || (o.ResourceType == expectation.ResourceType && o.ResourceId == expectation.ResourceId)) &&
            o.OccurredUtc >= baseline && o.OccurredUtc <= evaluationUtc)
            .Select(o => (DateTimeOffset?)o.OccurredUtc).Max();
        var windowStart = last ?? baseline;
        var deadline = last is null ? baseline + expectation.StartupGrace + expectation.MaximumSilence : last.Value + expectation.MaximumSilence;
        return new SecurityTelemetryEvaluationResult
        {
            ExpectationId = expectation.ExpectationId,
            WindowStartedUtc = windowStart,
            DeadlineUtc = deadline,
            LastObservedUtc = last,
            Status = evaluationUtc > deadline ? SecurityTelemetryEvaluationStatus.Missing :
                last is null ? SecurityTelemetryEvaluationStatus.AwaitingFirstObservation : SecurityTelemetryEvaluationStatus.ObservedWithinInterval
        };
    }
}
