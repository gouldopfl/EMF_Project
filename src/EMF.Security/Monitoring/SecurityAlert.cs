namespace EMF.Security.Monitoring;

public sealed class SecurityAlert
{
    public required string AlertId { get; init; }
    public required string AlertType { get; init; }
    public required SecurityAlertSeverity Severity { get; init; }
    public required string Operation { get; init; }
    public required DateTimeOffset ObservedUtc { get; init; }
    public required int EventCount { get; init; }
    public required DateTimeOffset WindowStartedUtc { get; init; }
    public IReadOnlyDictionary<string, string> Facts { get; init; } =
        new Dictionary<string, string>(
            StringComparer.Ordinal);
}
