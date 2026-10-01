namespace EMF.Security.Monitoring;

public enum SecurityAuditIntegrityFailureCategory
{
    RecordHashMismatch,
    UnsupportedIntegrityVersion,
    MissingRecordHash,
    PreviousHashMismatch,
    InvalidLegacyPlacement
}

/// <summary>Verification diagnostics only; counts on failure describe the verified prefix.</summary>
public sealed record SecurityAuditIntegrityDiagnostics
{
    public required bool IsValid { get; init; }
    public SecurityAuditIntegrityFailureCategory? FailureCategory { get; init; }
    public long? FirstInvalidRecordId { get; init; }
    public required int ProtectedPrefixCount { get; init; }
    public required int LegacyPrefixCount { get; init; }
}

public sealed record SecurityAuditIntegrityAlertPolicy
{
    public required string AuditSourceId { get; init; }
    public required SecurityAlertSeverity Severity { get; init; }
}

public sealed record SecurityTelemetryMaintenanceInterval
{
    public required DateTimeOffset StartedUtc { get; init; }
    /// <summary>Also the explicit baseline of the resumed active period.</summary>
    public required DateTimeOffset EndedUtc { get; init; }
}

public sealed record SecurityTelemetryExpectation
{
    public required string ExpectationId { get; init; }
    public required string SourceId { get; init; }
    public required string Operation { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public required TimeSpan MaximumSilence { get; init; }
    /// <summary>Deployment-persisted activation baseline; re-enabling requires a new baseline.</summary>
    public required DateTimeOffset EffectiveFromUtc { get; init; }
    public required TimeSpan StartupGrace { get; init; }
    public required bool Enabled { get; init; }
    public required SecurityAlertSeverity Severity { get; init; }
    public IReadOnlyList<SecurityTelemetryMaintenanceInterval> MaintenanceIntervals { get; init; } = [];
}

/// <summary>Supplied only by a trusted adapter after full chain verification; never contains audit facts.</summary>
public sealed record SecurityTelemetryObservation
{
    public required string SourceId { get; init; }
    public required string Operation { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required long RecordId { get; init; }
    public required DateTimeOffset OccurredUtc { get; init; }
}

public enum SecurityTelemetryEvaluationStatus
{
    Disabled, NotExpected, Suspended, AwaitingFirstObservation,
    ObservedWithinInterval, Missing, EvidenceUntrusted, MonitoringUnavailable
}

public sealed record SecurityTelemetryEvaluationResult
{
    public required string ExpectationId { get; init; }
    public required SecurityTelemetryEvaluationStatus Status { get; init; }
    public DateTimeOffset? WindowStartedUtc { get; init; }
    public DateTimeOffset? DeadlineUtc { get; init; }
    public DateTimeOffset? LastObservedUtc { get; init; }
    /// <summary>Internal failure context, never serialized into an alert.</summary>
    public Exception? Failure { get; init; }
}

public sealed record SecurityAuditMonitoringSnapshot
{
    public required string SourceId { get; init; }
    public required SecurityAuditIntegrityDiagnostics Integrity { get; init; }
    public IReadOnlyList<SecurityTelemetryObservation> Observations { get; init; } = [];
}

public interface ISecurityAuditMonitoringReader
{
    Task<SecurityAuditMonitoringSnapshot> ReadAsync(
        IReadOnlyList<SecurityTelemetryExpectation> expectations,
        DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken = default);
}

public sealed class SecurityAuditMonitoringUnavailableException : Exception
{
    public SecurityAuditMonitoringUnavailableException(Exception innerException)
        : base("Security audit monitoring could not read evidence.", innerException) { }
}

public sealed class SecurityTelemetryEvidenceException : Exception
{
    public SecurityTelemetryEvidenceException() : base("Security telemetry observation timestamp is invalid.") { }
}

public sealed class SecurityAlertDeliveryException : Exception
{
    public SecurityAlert Alert { get; }
    public SecurityAuditIntegrityDiagnostics? IntegrityFailure { get; }
    public SecurityAlertDeliveryException(SecurityAlert alert, SecurityAuditIntegrityDiagnostics? integrityFailure, Exception innerException)
        : base("Security alert was detected but delivery failed.", innerException)
    { Alert = alert; IntegrityFailure = integrityFailure; }
}
