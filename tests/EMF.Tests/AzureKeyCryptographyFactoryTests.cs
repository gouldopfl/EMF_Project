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
                KeyVersion = "0123456789abcdef0123456789abcdef"
            });

        Assert.NotNull(result);
        Assert.IsType<AzureKeyVaultCryptography>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("emf/key")]
    [InlineData("emf_key")]
    public void Create_RejectsInvalidKeyName(
        string keyName)
    {
        var factory = CreateFactory();

        var reference = new AzureKeyReference
        {
            KeyName = keyName,
            KeyVersion =
                "0123456789abcdef0123456789abcdef"
        };

        Assert.Throws<ArgumentException>(
            () => factory.Create(reference));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void Create_RejectsInvalidKeyVersion(
        string keyVersion)
    {
        var factory = CreateFactory();

        var reference = new AzureKeyReference
        {
            KeyName = "emf-key",
            KeyVersion = keyVersion
        };

        Assert.Throws<ArgumentException>(
            () => factory.Create(reference));
    }

    [Fact]
    public void Create_RejectsMissingKeyVersion()
    {
        var factory = CreateFactory();

        var reference = new AzureKeyReference
        {
            KeyName = "emf-key",
            KeyVersion = null
        };

        Assert.Throws<ArgumentException>(
            () => factory.Create(reference));
    }

    private static AzureKeyCryptographyFactory CreateFactory()
    {
        return new AzureKeyCryptographyFactory(
            new AzureKeyVaultOptions
            {
                VaultUri =
                    "https://example.vault.azure.net/"
            });
    }
}
