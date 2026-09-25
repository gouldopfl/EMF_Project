using System.Globalization;
using EMF.Security.Auditing.Models;

namespace EMF.Security.Azure.Monitoring;

internal static class SecurityAlertFactSanitizer
{
    public static IReadOnlyDictionary<string, string> Sanitize(
        IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // Only the existing threshold evaluator's bounded diagnostic values
        // cross this external boundary. A secret-name blacklist cannot identify
        // arbitrary clinical text or PHI hidden in otherwise ordinary fields.
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in facts)
        {
            var allowed = pair.Key switch
            {
                "outcome" => Enum.GetNames<SecurityAuditOutcome>()
                    .Contains(pair.Value, StringComparer.Ordinal),
                "threshold" => int.TryParse(pair.Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var threshold) && threshold > 0,
                "chainHeadHash" => pair.Value is { Length: 64 } &&
                    pair.Value.All(char.IsAsciiHexDigit),
                _ => false
            };
            if (allowed) result.Add(pair.Key, pair.Value);
        }
        return result;
    }
}
