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
}
