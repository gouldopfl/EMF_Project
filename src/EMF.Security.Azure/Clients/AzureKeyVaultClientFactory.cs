using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using EMF.Security.Azure.Configuration;

namespace EMF.Security.Azure.Clients;

public sealed class AzureKeyVaultClientFactory :
    IAzureKeyVaultClientFactory
{
    private readonly AzureKeyVaultOptions _options;

    public AzureKeyVaultClientFactory(
        AzureKeyVaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.VaultUri))
        {
            throw new ArgumentException(
                "Vault URI is required.",
                nameof(options));
        }

        _options = options;
    }

    public KeyClient CreateKeyClient()
    {
        return new KeyClient(
            new Uri(_options.VaultUri),
            new DefaultAzureCredential());
    }
}
