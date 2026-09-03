using EMF.Intelligence.AzureOpenAI.Clients;
using EMF.Intelligence.AzureOpenAI.Configuration;

namespace EMF.Tests;

public sealed class AzureOpenAIClientFactoryTests
{
    [Fact]
    public void CreateClient_UsesTokenCredentialConfiguration()
    {
        var factory =
            new AzureOpenAIClientFactory(
                CreateOptions(
                    "https://example.openai.azure.com"));

        var method =
            typeof(AzureOpenAIClientFactory)
                .GetMethod("CreateClient");

        var client = method!.Invoke(factory, null);

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData("http://example.openai.azure.com")]
    [InlineData("https://example.com")]
    [InlineData("https://openai.azure.com")]
    [InlineData("https://example.openai.azure.com.evil.test")]
    [InlineData("https://example.openai.azure.com:444")]
    [InlineData("https://example.openai.azure.com/path")]
    [InlineData("https://user@example.openai.azure.com")]
    [InlineData("https://example.openai.azure.com/?query=1")]
    [InlineData("https://example.openai.azure.com/#fragment")]
    public void Constructor_RejectsUntrustedEndpoint(
        string endpoint)
    {
        var options =
            CreateOptions(
                endpoint);

        Assert.Throws<ArgumentException>(
            () => new AzureOpenAIClientFactory(
                options));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{11111111-1111-1111-1111-111111111111}")]
    public void Constructor_RejectsInvalidManagedIdentityClientId(
        string clientId)
    {
        var options = CreateOptions(
            "https://example.openai.azure.com",
            clientId);

        Assert.Throws<ArgumentException>(
            () => new AzureOpenAIClientFactory(options));
    }

    [Fact]
    public void Constructor_AcceptsValidManagedIdentityClientId()
    {
        var options = CreateOptions(
            "https://example.openai.azure.com",
            "11111111-1111-1111-1111-111111111111");

        var factory =
            new AzureOpenAIClientFactory(options);

        Assert.NotNull(factory);
    }

    private static AzureOpenAIOptions CreateOptions(
        string endpoint,
        string? managedIdentityClientId = null)
    {
        return new AzureOpenAIOptions
        {
            Endpoint = endpoint,
            DeploymentName = "test-deployment",
            ProviderId = "azure.openai",
            ManagedIdentityClientId =
                managedIdentityClientId
        };
    }
}
