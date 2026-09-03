using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Keys;

namespace EMF.Security.Azure.Cryptography;

public sealed class AzureKeyCryptographyFactory : IAzureKeyCryptographyFactory
{
    private readonly AzureKeyVaultOptions _options;

    public AzureKeyCryptographyFactory(AzureKeyVaultOptions options)
    {
        _ = AzureKeyVaultOptionsValidator
            .ValidateVaultUri(options);

        _options = options;
    }

    public IAzureKeyCryptography Create(AzureKeyReference keyReference)
    {
        ArgumentNullException.ThrowIfNull(keyReference);

        AzureKeyVaultOptionsValidator.ValidateKeyName(
            keyReference.KeyName);
        AzureKeyVaultOptionsValidator.ValidateKeyVersion(
            keyReference.KeyVersion);

        var vault = _options.VaultUri.TrimEnd('/');
        var uri = new Uri(
            $"{vault}/keys/{keyReference.KeyName}/{keyReference.KeyVersion}");

        var client = new CryptographyClient(
            uri,
            new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

        return new AzureKeyVaultCryptography(
            new AzureCryptographyClientAdapter(client));
    }
}
