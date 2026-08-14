using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task LocalStack_ExecutesAgentThroughCapability()
    {
        var provider =
            new DevelopmentTextSummarizationProvider();

        var authorizationPolicy =
            new RecordingAuthorizationPolicy();

        var auditSink =
            new RecordingAuditSink();

        var classificationId =
            new ProtectionClassificationId(
                "confidential");

        var routingPolicy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        provider.Id,
                        classificationId)
                ]);

        var capabilityExecutor =
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

        var agent =
            new TextSummarizationAgent(
                capabilityExecutor);

        var agentExecutor =
            new IntelligenceAgentExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceAgentRegistry<
                    TextSummarizationRequest,
                    string>(
                    [agent]),
                auditSink);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "local-agent-operation-001"),
                classificationId,
                [
                    new ArtifactId(
                        "artifact-001")
                ],
                agent.Id);

        var result =
            await agentExecutor.ExecuteAsync(
                agent.Id,
                new TextSummarizationRequest(
                    "Alpha beta gamma",
                    11),
                context);

        Assert.Equal(
            "Alpha beta…",
            result.Output);

        Assert.Equal(
            provider.ProviderId,
            Assert.Single(
                result.CapabilityExecutions)
                .ProviderId);

        Assert.Single(
            authorizationPolicy.Requests);

        Assert.Equal(
            2,
            auditSink.Records.Count);

        Assert.Equal(
            "IntelligenceCapability.Execute",
            auditSink.Records[0].Operation);

        Assert.Equal(
            "IntelligenceAgent.Execute",
            auditSink.Records[1].Operation);

        Assert.All(
            auditSink.Records,
            audit =>
                Assert.Equal(
                    context.CorrelationId.Value,
                    audit.Facts["correlationId"]));
    }
}
