using EMF.Core.Contracts.Storage;
using EMF.Persistence.Storage;
using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Security.Storage;

namespace EMF.ConsoleApplication;

internal static class ArtifactContentStoreFactory
{
    public static IArtifactContentStore? Create()
    {
        return Create(
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_KEY_VAULT_URI"),
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_KEY_NAME"),
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_KEY_VERSION"),
            Environment.GetEnvironmentVariable(
                "EMF_ARTIFACT_CONTENT_PATH"));
    }

    internal static IArtifactContentStore? Create(
        string? vaultUri,
        string? keyName,
        string? keyVersion,
        string? contentPath)
    {
        var hasVaultUri =
            !string.IsNullOrWhiteSpace(vaultUri);
        var hasKeyName =
            !string.IsNullOrWhiteSpace(keyName);
        var hasKeyVersion =
            !string.IsNullOrWhiteSpace(keyVersion);

        if (!hasVaultUri &&
            !hasKeyName &&
            !hasKeyVersion)
        {
            return null;
        }

        if (!hasVaultUri ||
            !hasKeyName ||
            !hasKeyVersion)
        {
            throw new InvalidOperationException(
                "Azure Key Vault configuration must include " +
                "the vault URI, key name, and key version.");
        }

        var options = new AzureKeyVaultOptions
        {
            VaultUri = vaultUri!,
            KeyName = keyName,
            KeyVersion = keyVersion
        };

        var encryptionService =
            new AzureEnvelopeEncryptionService(
                new ConfiguredAzureKeyReferenceProvider(options),
                new AzureKeyCryptographyFactory(options));

        if (string.IsNullOrWhiteSpace(contentPath))
        {
            contentPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "artifact-content");
        }

        return new EncryptedArtifactContentStore(
            new FileSystemArtifactContentStore(contentPath),
            encryptionService);
    }
}
