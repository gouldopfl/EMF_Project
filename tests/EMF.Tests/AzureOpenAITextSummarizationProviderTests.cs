using EMF.Core.Models.Identities;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Providers;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class
    AzureOpenAITextSummarizationProviderTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsNormalizedResult()
    {
        var client =
            new RecordingAzureOpenAITextClient();

        var provider =
            new AzureOpenAITextSummarizationProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "summary-deployment",
                    ProviderId = "azure.openai"
                });

        var artifactId =
            new ArtifactId("artifact-001");

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [artifactId]);

        var result =
            await provider.ExecuteAsync(
                new TextSummarizationRequest(
                    "Source evidence text.",
                    100),
                context);

        Assert.True(result.Success);
        Assert.Equal("summary", result.Output);
        Assert.True(result.RequiresReview);

        Assert.Equal(
            IntelligenceCapabilityIds
                .TextSummarization,
            result.Metadata.CapabilityId);
        Assert.Equal(
            "azure.openai",
            result.Metadata.ProviderId.Value);
        Assert.Equal(
            "summary-deployment",
            result.Metadata.EngineName);
        Assert.Equal(
            "gpt-test",
            result.Metadata.EngineVersion);
        Assert.Equal(
            "operation-001",
            result.Metadata.ProviderOperationId);

        Assert.Equal(
            context.CorrelationId,
            result.Metadata.CorrelationId);
        Assert.NotEqual(
            default,
            result.Metadata.StartedUtc);
        Assert.NotEqual(
            default,
            result.Metadata.CompletedUtc);
        Assert.True(
            result.Metadata.CompletedUtc >=
            result.Metadata.StartedUtc);
        Assert.Equal(
            artifactId,
            Assert.Single(
                result.SourceArtifactIds));

        Assert.Contains(
            "100 characters",
            client.SystemInstruction);
        Assert.Equal(
            "Source evidence text.",
            client.Input);
        Assert.Equal(
            16,
            client.MaximumOutputTokenCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("summary too long")]
    public async Task ExecuteAsync_RejectsInvalidOutput(
        string output)
    {
        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion =
                    new(
                        output,
                        FinishReason: "Stop")
            };

        var provider =
            new AzureOpenAITextSummarizationProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "summary-deployment",
                    ProviderId = "azure.openai"
                });

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-002"),
                new ProtectionClassificationId(
                    "confidential"),
                []);

        await Assert.ThrowsAsync<
            EMF.Intelligence.AzureOpenAI.Exceptions.
                AzureOpenAIInvalidResponseException>(
            () => provider.ExecuteAsync(
                new TextSummarizationRequest(
                    "Source evidence text.",
                    7),
                context));
    }

    [Fact]
    public async Task
        ExecuteAsync_RecordsNonStopFinishReasonWarning()
    {
        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion =
                    new(
                        "summary",
                        FinishReason: "Length")
            };

        var provider =
            new AzureOpenAITextSummarizationProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "summary-deployment",
                    ProviderId = "azure.openai"
                });

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-warning"),
                new ProtectionClassificationId(
                    "confidential"),
                []);

        var result =
            await provider.ExecuteAsync(
                new TextSummarizationRequest(
                    "Source evidence text.",
                    100),
                context);

        Assert.True(result.Success);
        Assert.Equal("summary", result.Output);

        Assert.Equal(
            "Provider completion finished with reason 'Length'.",
            Assert.Single(result.Warnings));

        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task
        ExecuteAsync_PropagatesCancellationWithoutInvocation()
    {
        var client =
            new RecordingAzureOpenAITextClient();

        var provider =
            new AzureOpenAITextSummarizationProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "summary-deployment",
                    ProviderId = "azure.openai"
                });

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-cancelled"),
                new ProtectionClassificationId(
                    "confidential"),
                []);

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => provider.ExecuteAsync(
                new TextSummarizationRequest(
                    "Source evidence text.",
                    100),
                context,
                cancellation.Token));

        Assert.Null(client.SystemInstruction);
        Assert.Null(client.Input);
    }
    [Fact]
    public async Task ExecuteAsync_ExposesTokenAndCostTelemetry()
    {
        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion =
                    new(
                        "summary",
                        "gpt-test",
                        "operation-telemetry",
                        "Stop",
                        InputTokenCount: 100,
                        OutputTokenCount: 20,
                        TotalTokenCount: 120,
                        InputCostUsdPerMillionTokens: 2m,
                        OutputCostUsdPerMillionTokens: 8m,
                        EstimatedCostUsd: 0.00036m)
            };

        var provider =
            new AzureOpenAITextSummarizationProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "summary-deployment",
                    ProviderId = "azure.openai"
                });

        var result =
            await provider.ExecuteAsync(
                new TextSummarizationRequest(
                    "Source evidence text.",
                    100),
                new IntelligenceExecutionContext(
                    "security-steward",
                    new IntelligenceCorrelationId(
                        "operation-telemetry"),
                    new ProtectionClassificationId(
                        "confidential"),
                    []));

        Assert.Equal(100, result.Metadata.InputTokenCount);
        Assert.Equal(20, result.Metadata.OutputTokenCount);
        Assert.Equal(120, result.Metadata.TotalTokenCount);
        Assert.Equal(
            2m,
            result.Metadata.InputCostUsdPerMillionTokens);
        Assert.Equal(
            8m,
            result.Metadata.OutputCostUsdPerMillionTokens);
        Assert.Equal(
            0.00036m,
            result.Metadata.EstimatedCostUsd);
    }

}
