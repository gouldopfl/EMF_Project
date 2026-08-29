using EMF.Intelligence.AzureOpenAI.Composition;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Providers;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class
    AzureOpenAIStructuredExtractionCompositionTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesStructuredExtraction()
    {
        var client =
            new RecordingAzureOpenAITextClient
            {
                Completion = new("""{"outcome":"Denied"}""")
            };

        var options =
            new AzureOpenAIOptions
            {
                Endpoint = "https://example.openai.azure.com",
                DeploymentName = "test-deployment",
                ProviderId = "azure.openai"
            };

        var composition =
            new AzureOpenAITextIntelligenceComposition(
                new AzureOpenAITextSummarizationProvider(
                    client,
                    options),
                new AzureOpenAITextStructuredExtractionProvider(
                    client,
                    options),
                new AuthorizationPolicy(
                    new InMemoryAuthorizationContextProvider(
                        Array.Empty<AuthorizationContext>())),
                new RecordingSecurityAuditSink(),
                [new ProtectionClassificationId("confidential")]);

        var result =
            await composition
                .TextStructuredExtractionCapabilityExecutor
                .ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextStructuredExtraction,
                    new TextStructuredExtractionRequest(
                        "Decision text.",
                        "Extract.",
                        """{"outcome":"string"}"""),
                    new IntelligenceExecutionContext(
                        "security-steward",
                        new IntelligenceCorrelationId(
                            "operation-structured"),
                        new ProtectionClassificationId(
                            "confidential"),
                        []));

        Assert.True(result.Success);
        Assert.Equal(
            """{"outcome":"Denied"}""",
            result.Output);
    }
}
