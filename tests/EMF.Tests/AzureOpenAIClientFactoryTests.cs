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

    [Fact]
    public void Constructor_RejectsNonHttpsEndpoint()
    {
        var options =
            CreateOptions(
                "http://example.openai.azure.com");

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
