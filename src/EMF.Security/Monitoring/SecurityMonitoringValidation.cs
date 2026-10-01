using System.Globalization;

namespace EMF.Security.Monitoring;

/// <summary>Bounded diagnostic formats shared by core builders and delivery adapters.</summary>
public static class SecurityMonitoringValidation
{
    public static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');
    public static bool IsOperation(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');
    public static string FormatUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public static bool IsUtc(string value) => value.Length == 33 &&
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) &&
        parsed.Offset == TimeSpan.Zero && FormatUtc(parsed) == value;
    public static void Validate(SecurityAuditIntegrityAlertPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!IsIdentifier(policy.AuditSourceId) || !Enum.IsDefined(policy.Severity))
            throw new ArgumentException("Audit source identity or severity is invalid.", nameof(policy));
    }
    public static void Validate(SecurityTelemetryExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        if (!IsIdentifier(expectation.ExpectationId) || !IsIdentifier(expectation.SourceId) ||
            !IsOperation(expectation.Operation) || !Enum.IsDefined(expectation.Severity))
            throw new ArgumentException("Telemetry identity, operation or severity is invalid.", nameof(expectation));
        if ((expectation.ResourceType is null) != (expectation.ResourceId is null) ||
            (expectation.ResourceType is not null && (string.IsNullOrWhiteSpace(expectation.ResourceType) ||
             expectation.ResourceType.Length > 128 || string.IsNullOrWhiteSpace(expectation.ResourceId) || expectation.ResourceId.Length > 256)))
            throw new ArgumentException("Resource selectors must be an exact bounded pair.", nameof(expectation));
        if (expectation.MaximumSilence <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(expectation.MaximumSilence));
        if (expectation.StartupGrace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(expectation.StartupGrace));
        _ = expectation.EffectiveFromUtc + expectation.StartupGrace + expectation.MaximumSilence;
        ArgumentNullException.ThrowIfNull(expectation.MaintenanceIntervals);
        DateTimeOffset? previousEnd = null;
        foreach (var interval in expectation.MaintenanceIntervals.OrderBy(i => i.StartedUtc))
        {
            if (interval.EndedUtc <= interval.StartedUtc || (previousEnd is not null && interval.StartedUtc < previousEnd))
                throw new ArgumentException("Maintenance intervals must be nonempty and nonoverlapping.", nameof(expectation));
            _ = interval.EndedUtc + expectation.StartupGrace + expectation.MaximumSilence;
            previousEnd = interval.EndedUtc;
        }
    }
}
