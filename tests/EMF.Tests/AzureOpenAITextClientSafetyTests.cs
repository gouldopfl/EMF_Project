using Azure.AI.OpenAI;
using EMF.Intelligence.AzureOpenAI.Clients;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Models;

namespace EMF.Tests;

public sealed class AzureOpenAITextClientSafetyTests
{
    [Fact]
    public async Task CompleteAsync_RejectsWhenLiveCallsAreDisabled()
    {
        var factory = new ThrowingClientFactory();

        var client =
            new AzureOpenAITextClient(
                factory,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "test-deployment",
                    ProviderId = "azure.openai"
                });

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.CompleteAsync(
                    "System instruction.",
                    "Input text."));

        Assert.Contains(
            "live calls are disabled",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(factory.CreateClientCalled);
    }

    [Fact]
    public void CostEstimator_ComputesConfiguredEstimate()
    {
        var result =
            AzureOpenAICostEstimator.EstimateUsd(
                inputTokenCount: 100,
                outputTokenCount: 20,
                inputCostUsdPerMillionTokens: 2m,
                outputCostUsdPerMillionTokens: 8m);

        Assert.Equal(0.00036m, result);
    }

    [Fact]
    public void CostEstimator_ReturnsNullWithoutRates()
    {
        var result =
            AzureOpenAICostEstimator.EstimateUsd(
                inputTokenCount: 100,
                outputTokenCount: 20,
                inputCostUsdPerMillionTokens: null,
                outputCostUsdPerMillionTokens: null);

        Assert.Null(result);
    }

    private sealed class ThrowingClientFactory :
        IAzureOpenAIClientFactory
    {
        public bool CreateClientCalled { get; private set; }

        public AzureOpenAIClient CreateClient()
        {
            CreateClientCalled = true;

            throw new InvalidOperationException(
                "Client creation should not occur when live calls are disabled.");
        }
    }
}
