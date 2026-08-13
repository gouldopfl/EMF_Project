using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Keys;

namespace EMF.Tests;

public sealed class ConfiguredAzureKeyReferenceProviderTests
{
    [Fact]
    public async Task GetCurrentKeyAsync_ReturnsConfiguredKey()
    {
        var provider = new ConfiguredAzureKeyReferenceProvider(
            new AzureKeyVaultOptions
            {
                VaultUri = "https://example.vault.azure.net/",
                KeyName = "emf-key",
                KeyVersion = "v1"
            });

        var result = await provider.GetCurrentKeyAsync();

        Assert.Equal("emf-key", result.KeyName);
        Assert.Equal("v1", result.KeyVersion);
    }
}
