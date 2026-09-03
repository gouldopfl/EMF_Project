using EMF.Security.Azure.Clients;
using EMF.Security.Azure.Configuration;

namespace EMF.Tests;

public sealed class AzureKeyVaultClientFactoryTests
{
    [Fact]
    public void Constructor_RejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureKeyVaultClientFactory(null!));
    }

    [Fact]
    public void Constructor_RejectsBlankVaultUri()
    {
        var options =
            new AzureKeyVaultOptions
            {
                VaultUri = " "
            };

        Assert.Throws<ArgumentException>(
            () => new AzureKeyVaultClientFactory(options));
    }


    [Theory]
    [InlineData("http://example.vault.azure.net/")]
    [InlineData("example.vault.azure.net")]
    [InlineData("https://user@example.vault.azure.net/")]
    [InlineData("https://example.vault.azure.net/?x=1")]
    [InlineData("https://example.vault.azure.net/#fragment")]
    [InlineData("https://example.vault.azure.net/not-root")]
    [InlineData("https://example.com/")]
    [InlineData("https://vault.azure.net/")]
    [InlineData("https://example.vault.azure.net.evil.test/")]
    [InlineData("https://example.vault.azure.net:444/")]
    public void Constructor_RejectsInvalidVaultUri(
        string vaultUri)
    {
        var options =
            new AzureKeyVaultOptions
            {
                VaultUri = vaultUri
            };

        Assert.Throws<ArgumentException>(
            () => new AzureKeyVaultClientFactory(options));
    }

    [Fact]
    public void CreateKeyClient_ReturnsClient()
    {
        var options =
            new AzureKeyVaultOptions
            {
                VaultUri = "https://example.vault.azure.net/"
            };

        var factory =
            new AzureKeyVaultClientFactory(options);

        var client =
            factory.CreateKeyClient();

        Assert.NotNull(client);
    }
}
