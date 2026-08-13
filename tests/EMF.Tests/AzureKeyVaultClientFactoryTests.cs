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
