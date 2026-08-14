using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RunsDevelopmentProviderEndToEnd()
    {
        var provider =
            new DevelopmentTextSummarizationProvider();

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-development-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [
                    new ArtifactId(
                        "artifact-001")
                ]);

        var authorizationPolicy =
            new RecordingAuthorizationPolicy();

        var auditSink =
            new RecordingAuditSink();

        var routingPolicy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        provider.Id,
                        context.ProtectionClassificationId)
                ]);

        var executor =
            new IntelligenceCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    TextSummarizationRequest,
                    string>(
                    [provider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var result =
            await executor.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextSummarization,
                new TextSummarizationRequest(
                    "Alpha beta gamma",
                    11),
                context);

        Assert.Equal(
            "Alpha beta…",
            result.Output);

        Assert.Single(
            authorizationPolicy.Requests);

        var audit =
            Assert.Single(
                auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Succeeded,
            audit.Outcome);

        Assert.Equal(
            provider.ProviderId.Value,
            audit.Destination);

        Assert.Equal(
            context.CorrelationId.Value,
            audit.Facts["correlationId"]);

        Assert.Equal(
            "deterministic-extractive",
            audit.Facts["engineName"]);
    }
}
