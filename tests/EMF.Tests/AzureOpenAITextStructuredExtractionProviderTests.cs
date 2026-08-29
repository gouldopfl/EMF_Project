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
    AzureOpenAITextStructuredExtractionProviderTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsValidatedJson()
    {
        const string json =
            """{"outcome":"Denied"}""";

        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion =
                    new(
                        json,
                        "gpt-test",
                        "operation-structured",
                        "Stop")
            };

        var provider =
            new AzureOpenAITextStructuredExtractionProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint =
                        "https://example.openai.azure.com",
                    DeploymentName = "extract-deployment",
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
                new TextStructuredExtractionRequest(
                    "Decision text.",
                    "Extract the decision.",
                    """{"outcome":"string"}"""),
                context);

        Assert.True(result.Success);
        Assert.Equal(json, result.Output);
        Assert.True(result.RequiresReview);

        Assert.Equal(
            IntelligenceCapabilityIds
                .TextStructuredExtraction,
            result.Metadata.CapabilityId);

        Assert.Equal(
            "Decision text.",
            client.Input);

        Assert.Contains(
            "Extract the decision.",
            client.SystemInstruction);

        Assert.Contains(
            """{"outcome":"string"}""",
            client.SystemInstruction);
    }
    [Fact]
    public async Task ExecuteAsync_RejectsInvalidJson()
    {
        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion = new("{ not-json")
            };

        var provider =
            new AzureOpenAITextStructuredExtractionProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint = "https://example.openai.azure.com",
                    DeploymentName = "extract-deployment",
                    ProviderId = "azure.openai"
                });

        await Assert.ThrowsAsync<
            EMF.Intelligence.AzureOpenAI.Exceptions.
                AzureOpenAIInvalidResponseException>(
                () => provider.ExecuteAsync(
                    new TextStructuredExtractionRequest(
                        "Decision text.",
                        "Extract.",
                        """{"outcome":"string"}"""),
                    TestContext()));
    }


    [Fact]
    public async Task ExecuteAsync_RecordsNonStopFinishReasonWarning()
    {
        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion =
                    new(
                        """{"outcome":"Denied"}""",
                        "gpt-test",
                        "operation-structured",
                        "Length")
            };

        var provider =
            new AzureOpenAITextStructuredExtractionProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint = "https://example.openai.azure.com",
                    DeploymentName = "extract-deployment",
                    ProviderId = "azure.openai"
                });

        var result =
            await provider.ExecuteAsync(
                new TextStructuredExtractionRequest(
                    "Decision text.",
                    "Extract.",
                    """{"outcome":"string"}"""),
                TestContext());

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("Length"));
    }


    [Fact]
    public async Task ExecuteAsync_HonorsCancellation()
    {
        var client =
            new RecordingAzureOpenAITextClient();

        var provider =
            new AzureOpenAITextStructuredExtractionProvider(
                client,
                new AzureOpenAIOptions
                {
                    Endpoint = "https://example.openai.azure.com",
                    DeploymentName = "extract-deployment",
                    ProviderId = "azure.openai"
                });

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExecuteAsync(
                new TextStructuredExtractionRequest(
                    "Decision text.",
                    "Extract.",
                    """{"outcome":"string"}"""),
                TestContext(),
                cancellation.Token));

        Assert.Null(client.Input);
    }

    private static IntelligenceExecutionContext TestContext() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("operation-001"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());

}
