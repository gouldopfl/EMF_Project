using System.Globalization;
using EMF.Security.Auditing.Models;
using EMF.Security.Monitoring;

namespace EMF.Security.Azure.Monitoring;

internal static class SecurityAlertFactSanitizer
{
    public static IReadOnlyDictionary<string, string> Sanitize(
        string alertType, IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // Each alert family has a separate bounded schema. No arbitrary facts cross this boundary.
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in facts)
        {
            var allowed = alertType switch
            {
                "audit.integrity-failure" => pair.Key switch
                {
                    "AuditSourceId" => SecurityMonitoringValidation.IsIdentifier(pair.Value),
                    "FailureCategory" => Enum.GetNames<SecurityAuditIntegrityFailureCategory>().Contains(pair.Value, StringComparer.Ordinal),
                    "FirstInvalidRecordId" => pair.Value.Length <= 19 && long.TryParse(pair.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0,
                    "ProtectedPrefixCount" or "LegacyPrefixCount" => pair.Value.Length <= 10 && int.TryParse(pair.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0,
                    "VerificationUtc" => SecurityMonitoringValidation.IsUtc(pair.Value),
                    _ => false
                },
                "telemetry.required-missing" => pair.Key switch
                {
                    "ExpectationId" or "SourceId" => SecurityMonitoringValidation.IsIdentifier(pair.Value),
                    "Operation" => SecurityMonitoringValidation.IsOperation(pair.Value),
                    "EvaluationUtc" or "DeadlineUtc" or "LastObservedUtc" => SecurityMonitoringValidation.IsUtc(pair.Value),
                    _ => false
                },
                _ => pair.Key switch
                {
                    "outcome" => Enum.GetNames<SecurityAuditOutcome>()
                        .Contains(pair.Value, StringComparer.Ordinal),
                    "threshold" => int.TryParse(pair.Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var threshold) && threshold > 0,
                    "chainHeadHash" => pair.Value is { Length: 64 } &&
                        pair.Value.All(char.IsAsciiHexDigit),
                    _ => false
                }
            };
            if (allowed) result.Add(pair.Key, pair.Value);
        }
        return result;
    }
}
