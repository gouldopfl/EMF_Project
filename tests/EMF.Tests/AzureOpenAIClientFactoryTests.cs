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

    private static AzureOpenAIOptions CreateOptions(
        string endpoint)
    {
        return new AzureOpenAIOptions
        {
            Endpoint = endpoint,
            DeploymentName = "test-deployment",
            ProviderId = "azure.openai"
        };
    }
}
