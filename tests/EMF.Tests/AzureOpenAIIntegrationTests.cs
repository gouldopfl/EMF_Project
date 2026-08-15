using EMF.Intelligence.AzureOpenAI.Composition;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class AzureOpenAIIntegrationTests
{
    [AzureOpenAIIntegrationFact]
    [Trait("Category", "AzureIntegration")]
    public async Task ExecuteAsync_ReturnsLiveNormalizedSummary()
    {
        var endpoint =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_OPENAI_ENDPOINT")!;

        var deployment =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_OPENAI_DEPLOYMENT")!;

        var classification =
            new ProtectionClassificationId("public");

        var composition =
            new AzureOpenAITextIntelligenceComposition(
                new AzureOpenAIOptions
                {
                    Endpoint = endpoint,
                    DeploymentName = deployment,
                    ProviderId = "azure.openai",
                    ManagedIdentityClientId =
                        Environment.GetEnvironmentVariable(
                            "EMF_AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID")
                },
                new AuthorizationPolicy(
                    new InMemoryAuthorizationContextProvider(
                        Array.Empty<AuthorizationContext>())),
                new RecordingSecurityAuditSink(),
                [classification]);

        var correlationId =
            new IntelligenceCorrelationId(
                $"azure-live-{Guid.NewGuid():N}");

        var result =
            await composition
                .TextSummarizationCapabilityExecutor
                .ExecuteAsync(
                    IntelligenceCapabilityIds.TextSummarization,
                    new TextSummarizationRequest(
                        "Evidence intelligence supports traceable, " +
                        "review-gated analysis.",
                        500),
                    new IntelligenceExecutionContext(
                        "integration-steward",
                        correlationId,
                        classification,
                        []));

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.True(result.Output!.Length <= 500);
        Assert.True(result.RequiresReview);
        Assert.Equal(
            "azure.openai",
            result.Metadata.ProviderId.Value);
        Assert.Equal(
            deployment,
            result.Metadata.EngineName);
        Assert.False(
            string.IsNullOrWhiteSpace(
                result.Metadata.EngineVersion));
        Assert.Equal(
            correlationId,
            result.Metadata.CorrelationId);
    }
}
