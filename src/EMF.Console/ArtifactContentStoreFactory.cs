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
        var vaultUri =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_KEY_VAULT_URI");

        var keyName =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_KEY_NAME");

        var keyVersion =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_KEY_VERSION");

        if (string.IsNullOrWhiteSpace(vaultUri) ||
            string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        var options = new AzureKeyVaultOptions
        {
            VaultUri = vaultUri,
            KeyName = keyName,
            KeyVersion = keyVersion
        };

        var encryptionService =
            new AzureEnvelopeEncryptionService(
                new ConfiguredAzureKeyReferenceProvider(options),
                new AzureKeyCryptographyFactory(options));

        var contentPath =
            Environment.GetEnvironmentVariable(
                "EMF_ARTIFACT_CONTENT_PATH");

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
