using EMF.Intelligence.AzureOpenAI.Clients;
using EMF.Intelligence.AzureOpenAI.Configuration;

namespace EMF.Tests;

public sealed class AzureOpenAIOptionsTests
{
    [Fact]
    public void Options_DoNotExposeApiKeyConfiguration()
    {
        var properties =
            typeof(AzureOpenAIOptions)
                .GetProperties();

        Assert.DoesNotContain(
            properties,
            property => property.Name.Contains(
                "ApiKey",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Factory_RequiresDeploymentName()
    {
        var options =
            CreateOptions(deploymentName: " ");

        Assert.Throws<ArgumentException>(
            () => new AzureOpenAIClientFactory(
                options));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Factory_RejectsUnboundedRetryValues(
        int maximumRetries)
    {
        var options =
            CreateOptions(
                maximumRetries: maximumRetries);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AzureOpenAIClientFactory(
                options));
    }

    private static AzureOpenAIOptions CreateOptions(
        string deploymentName = "test-deployment",
        int maximumRetries = 2)
    {
        return new AzureOpenAIOptions
        {
            Endpoint =
                "https://example.openai.azure.com",
            DeploymentName = deploymentName,
            ProviderId = "azure.openai",
            MaximumRetries = maximumRetries
        };
    }

    [Fact]
    public void Factory_RequiresProviderId()
    {
        var options =
            new AzureOpenAIOptions
            {
                Endpoint =
                    "https://example.openai.azure.com",
                DeploymentName = "test-deployment",
                ProviderId = " "
            };

        Assert.Throws<ArgumentException>(
            () => new AzureOpenAIClientFactory(
                options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Factory_RejectsNonPositiveRequestTimeout(
        int timeoutMilliseconds)
    {
        var options =
            new AzureOpenAIOptions
            {
                Endpoint =
                    "https://example.openai.azure.com",
                DeploymentName = "test-deployment",
                ProviderId = "azure.openai",
                RequestTimeout =
                    TimeSpan.FromMilliseconds(
                        timeoutMilliseconds)
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AzureOpenAIClientFactory(
                options));
    }
}
