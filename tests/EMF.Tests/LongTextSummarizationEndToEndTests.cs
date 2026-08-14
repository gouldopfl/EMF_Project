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
    public async Task LocalStack_SummarizesSegmentedText()
    {
        var segmentationProvider =
            new DevelopmentTextSegmentationProvider();

        var summarizationProvider =
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
                        segmentationProvider.ProviderId,
                        segmentationProvider.Id,
                        classificationId),
                    new IntelligenceProviderRoutingGrant(
                        summarizationProvider.ProviderId,
                        summarizationProvider.Id,
                        classificationId)
                ]);

        var segmentationExecutor =
            new IntelligenceCapabilityExecutor<
                TextSegmentationRequest,
                IReadOnlyList<TextSegment>>(
                new IntelligenceCapabilityProviderRouter<
                    TextSegmentationRequest,
                    IReadOnlyList<TextSegment>>(
                    [segmentationProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var summarizationExecutor =
            new IntelligenceCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    TextSummarizationRequest,
                    string>(
                    [summarizationProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var agent =
            new LongTextSummarizationAgent(
                segmentationExecutor,
                summarizationExecutor);

        var agentExecutor =
            new IntelligenceAgentExecutor<
                LongTextSummarizationRequest,
                string>(
                new IntelligenceAgentRegistry<
                    LongTextSummarizationRequest,
                    string>(
                    [agent]),
                auditSink);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "local-long-text-operation-001"),
                classificationId,
                [
                    new ArtifactId(
                        "artifact-001")
                ],
                agent.Id);

        var result =
            await agentExecutor.ExecuteAsync(
                agent.Id,
                new LongTextSummarizationRequest(
                    "ABCDEFGHIJ",
                    4,
                    1,
                    3),
                context);

        Assert.True(result.Success);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "AB…",
                "DE…",
                "GH…"),
            result.Output);

        Assert.Equal(
            4,
            result.CapabilityExecutions.Count);

        Assert.All(
            result.CapabilityExecutions,
            execution =>
                Assert.Equal(
                    new IntelligenceProviderId(
                        "development.local"),
                    execution.ProviderId));

        Assert.Equal(
            4,
            authorizationPolicy.Requests.Count);

        Assert.Equal(
            4,
            auditSink.Records.Count(
                record =>
                    record.Operation ==
                    "IntelligenceCapability.Execute"));

        Assert.Single(
            auditSink.Records.Where(
                record =>
                    record.Operation ==
                    "IntelligenceAgent.Execute"));

        Assert.All(
            auditSink.Records,
            audit =>
                Assert.Equal(
                    context.CorrelationId.Value,
                    audit.Facts["correlationId"]));

        Assert.Equal(2, result.Warnings.Count);
        Assert.True(result.RequiresReview);
    }
}
