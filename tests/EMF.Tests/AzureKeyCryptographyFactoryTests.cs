using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Keys;

namespace EMF.Tests;

public sealed class AzureKeyCryptographyFactoryTests
{

    [Fact]
    public void Constructor_RejectsInvalidVaultUri()
    {
        var options = new AzureKeyVaultOptions
        {
            VaultUri = "http://example.vault.azure.net/"
        };

        Assert.Throws<ArgumentException>(
            () => new AzureKeyCryptographyFactory(options));
    }

    [Fact]
    public void Create_ReturnsCryptography()
    {
        var factory = new AzureKeyCryptographyFactory(
            new AzureKeyVaultOptions
            {
                VaultUri = "https://example.vault.azure.net/"
            });

        var result = factory.Create(
            new AzureKeyReference
            {
                KeyName = "emf-key",
                KeyVersion = "version1"
            });

        Assert.NotNull(result);
        Assert.IsType<AzureKeyVaultCryptography>(result);
    }
}
