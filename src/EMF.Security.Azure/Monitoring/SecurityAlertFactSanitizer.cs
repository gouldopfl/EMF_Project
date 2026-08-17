namespace EMF.Security.Azure.Monitoring;

internal static class SecurityAlertFactSanitizer
{
    private static readonly string[] SensitiveTerms =
    [
        "password",
        "token",
        "secret",
        "apikey",
        "api-key",
        "mfa",
        "otp",
        "keymaterial",
        "encryptionkey"
    ];

    public static IReadOnlyDictionary<string, string> Sanitize(
        IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return facts
            .Where(pair => !IsSensitive(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static bool IsSensitive(string key)
    {
        var normalized =
            key.Replace("_", "", StringComparison.Ordinal)
               .Replace("-", "", StringComparison.Ordinal)
               .Replace(".", "", StringComparison.Ordinal)
               .ToLowerInvariant();

        return SensitiveTerms.Any(
            term => normalized.Contains(
                term.Replace("-", "", StringComparison.Ordinal),
                StringComparison.Ordinal));
    }
}
