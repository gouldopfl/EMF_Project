namespace EMF.Security.Azure.Configuration;

internal static class AzureKeyVaultOptionsValidator
{
    public static Uri ValidateVaultUri(
        AzureKeyVaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(
                options.VaultUri,
                UriKind.Absolute,
                out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !uri.IsDefaultPort ||
            !uri.Host.EndsWith(
                ".vault.azure.net",
                StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Length <= ".vault.azure.net".Length ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Vault URI must be an absolute HTTPS root URI.",
                nameof(options));
        }

        return uri;
    }

    public static void ValidateKeyName(string? keyName)
    {
        if (!IsValidKeyName(keyName))
        {
            throw new ArgumentException(
                "Key name must contain 1 through 127 ASCII letters, digits, or hyphens.",
                nameof(keyName));
        }
    }

    public static void ValidateKeyVersion(string? keyVersion)
    {
        if (!IsValidKeyVersion(keyVersion))
        {
            throw new ArgumentException(
                "Key version must be a 32-character hexadecimal value.",
                nameof(keyVersion));
        }
    }

    public static bool IsValidKeyName(string? keyName)
    {
        return keyName is { Length: >= 1 and <= 127 } &&
            keyName.All(IsKeyNameCharacter);
    }

    public static bool IsValidKeyVersion(string? keyVersion)
    {
        return keyVersion is { Length: 32 } &&
            keyVersion.All(IsHexadecimalCharacter);
    }

    private static bool IsKeyNameCharacter(char value)
    {
        return value is >= 'a' and <= 'z' ||
            value is >= 'A' and <= 'Z' ||
            value is >= '0' and <= '9' ||
            value == '-';
    }

    private static bool IsHexadecimalCharacter(char value)
    {
        return value is >= 'a' and <= 'f' ||
            value is >= 'A' and <= 'F' ||
            value is >= '0' and <= '9';
    }
}
