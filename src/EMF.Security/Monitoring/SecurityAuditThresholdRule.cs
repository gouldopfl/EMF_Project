using EMF.Security.Auditing.Models;

namespace EMF.Security.Monitoring;

public sealed class SecurityAuditThresholdRule
{
    public required string AlertType { get; init; }
    public required string Operation { get; init; }
    public required SecurityAuditOutcome Outcome { get; init; }
    public required int Threshold { get; init; }
    public required TimeSpan Window { get; init; }
    public required SecurityAlertSeverity Severity { get; init; }
}
