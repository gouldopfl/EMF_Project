using System.Globalization;

namespace EMF.Security.Monitoring;

public sealed class SecurityAuditIntegrityAlertDetector
{
    public SecurityAlert? Build(SecurityAuditIntegrityDiagnostics verification,
        SecurityAuditIntegrityAlertPolicy policy, DateTimeOffset verificationUtc)
    {
        ArgumentNullException.ThrowIfNull(verification);
        SecurityMonitoringValidation.Validate(policy);
        if (verification.IsValid) return null;
        if (verification.FailureCategory is null || !Enum.IsDefined(verification.FailureCategory.Value) ||
            verification.ProtectedPrefixCount < 0 || verification.LegacyPrefixCount < 0 || verification.FirstInvalidRecordId <= 0)
            throw new ArgumentException("Invalid integrity diagnostic result.", nameof(verification));
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuditSourceId"] = policy.AuditSourceId,
            ["FailureCategory"] = verification.FailureCategory.Value.ToString(),
            ["ProtectedPrefixCount"] = verification.ProtectedPrefixCount.ToString(CultureInfo.InvariantCulture),
            ["LegacyPrefixCount"] = verification.LegacyPrefixCount.ToString(CultureInfo.InvariantCulture),
            ["VerificationUtc"] = SecurityMonitoringValidation.FormatUtc(verificationUtc)
        };
        if (verification.FirstInvalidRecordId is { } id) facts.Add("FirstInvalidRecordId", id.ToString(CultureInfo.InvariantCulture));
        return new SecurityAlert
        {
            AlertId = Guid.NewGuid().ToString("N"),
            AlertType = "audit.integrity-failure",
            Operation = "security.audit.verify",
            Severity = policy.Severity,
            ObservedUtc = verificationUtc.ToUniversalTime(),
            WindowStartedUtc = verificationUtc.ToUniversalTime(),
            EventCount = 1,
            Facts = facts
        };
    }
}
