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

    private static readonly string[] SensitiveValueMarkers =
    [
        "bearer ",
        "basic ",
        "password=",
        "pwd=",
        "client_secret=",
        "clientsecret=",
        "access_token=",
        "apikey=",
        "api-key=",
        "-----begin private key-----",
        "-----begin rsa private key-----"
    ];

    public static IReadOnlyDictionary<string, string> Sanitize(
        IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return facts
            .Where(
                pair =>
                    !IsSensitive(pair.Key) &&
                    !IsSensitiveValue(pair.Value))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static bool IsSensitiveValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return SensitiveValueMarkers.Any(
            marker => value.Contains(
                marker,
                StringComparison.OrdinalIgnoreCase));
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
