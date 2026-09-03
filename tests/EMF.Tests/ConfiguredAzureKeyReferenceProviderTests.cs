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
                KeyVersion = "0123456789abcdef0123456789abcdef"
            });

        var result = await provider.GetCurrentKeyAsync();

        Assert.Equal("emf-key", result.KeyName);
        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            result.KeyVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("emf/key")]
    [InlineData("emf_key")]
    [InlineData("emf key")]
    public void Constructor_RejectsInvalidKeyName(
        string keyName)
    {
        var options = new AzureKeyVaultOptions
        {
            VaultUri = "https://example.vault.azure.net/",
            KeyName = keyName,
            KeyVersion =
                "0123456789abcdef0123456789abcdef"
        };

        Assert.Throws<ArgumentException>(
            () =>
                new ConfiguredAzureKeyReferenceProvider(
                    options));
    }

    [Fact]
    public void Constructor_RejectsOverlengthKeyName()
    {
        var options = new AzureKeyVaultOptions
        {
            VaultUri = "https://example.vault.azure.net/",
            KeyName = new string('a', 128),
            KeyVersion =
                "0123456789abcdef0123456789abcdef"
        };

        Assert.Throws<ArgumentException>(
            () =>
                new ConfiguredAzureKeyReferenceProvider(
                    options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void Constructor_RejectsInvalidKeyVersion(
        string keyVersion)
    {
        var options = new AzureKeyVaultOptions
        {
            VaultUri = "https://example.vault.azure.net/",
            KeyName = "emf-key",
            KeyVersion = keyVersion
        };

        Assert.Throws<ArgumentException>(
            () =>
                new ConfiguredAzureKeyReferenceProvider(
                    options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("emf-key")]
    [InlineData("emf-key/v1")]
    [InlineData(
        "emf_key/0123456789abcdef0123456789abcdef")]
    [InlineData(
        "emf-key/0123456789abcdef0123456789abcdef/extra")]
    public async Task GetKeyAsync_ReturnsNullForMalformedIdentifier(
        string keyIdentifier)
    {
        var provider = new ConfiguredAzureKeyReferenceProvider(
            new AzureKeyVaultOptions
            {
                VaultUri =
                    "https://example.vault.azure.net/",
                KeyName = "emf-key",
                KeyVersion =
                    "0123456789abcdef0123456789abcdef"
            });

        var result =
            await provider.GetKeyAsync(keyIdentifier);

        Assert.Null(result);
    }
}
