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
}
