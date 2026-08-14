using EMF.Intelligence.AzureOpenAI.Composition;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Providers;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class AzureOpenAICompositionTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsUnauthorizedClassificationBeforeInvocation()
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

        var auditSink =
            new RecordingSecurityAuditSink();

        var composition =
            new AzureOpenAITextIntelligenceComposition(
                provider,
                new AuthorizationPolicy(
                    new InMemoryAuthorizationContextProvider(
                        Array.Empty<
                            AuthorizationContext>())),
                auditSink,
                [
                    new ProtectionClassificationId(
                        "confidential")
                ]);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-denied"),
                new ProtectionClassificationId(
                    "restricted"),
                []);

        await Assert.ThrowsAsync<
            IntelligenceProviderUnavailableException>(
            () => composition
                .TextSummarizationCapabilityExecutor
                .ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextSummarization,
                    new TextSummarizationRequest(
                        "Protected source text.",
                        100),
                    context));

        Assert.Null(client.Input);
        Assert.Single(auditSink.Records);
        Assert.Equal(
            "Denied",
            auditSink.Records[0].Outcome.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_InvokesProviderForPermittedClassification()
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

        var auditSink =
            new RecordingSecurityAuditSink();

        var classification =
            new ProtectionClassificationId(
                "confidential");

        var composition =
            new AzureOpenAITextIntelligenceComposition(
                provider,
                new AuthorizationPolicy(
                    new InMemoryAuthorizationContextProvider(
                        Array.Empty<
                            AuthorizationContext>())),
                auditSink,
                [classification]);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-permitted"),
                classification,
                []);

        var result =
            await composition
                .TextSummarizationCapabilityExecutor
                .ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextSummarization,
                    new TextSummarizationRequest(
                        "Permitted source text.",
                        100),
                    context);

        Assert.True(result.Success);
        Assert.Equal(
            "Permitted source text.",
            client.Input);
        Assert.Single(auditSink.Records);
        Assert.Equal(
            "Succeeded",
            auditSink.Records[0].Outcome.ToString());
    }
}
