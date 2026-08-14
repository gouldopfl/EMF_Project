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
    public async Task LocalStack_GeneratesLongTextInsight()
    {
        var segmentationProvider =
            new DevelopmentTextSegmentationProvider();

        var summarizationProvider =
            new DevelopmentTextSummarizationProvider();

        var keywordProvider =
            new DevelopmentTextKeywordExtractionProvider();

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
                        classificationId),
                    new IntelligenceProviderRoutingGrant(
                        keywordProvider.ProviderId,
                        keywordProvider.Id,
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

        var keywordExecutor =
            new IntelligenceCapabilityExecutor<
                TextKeywordExtractionRequest,
                IReadOnlyList<TextKeyword>>(
                new IntelligenceCapabilityProviderRouter<
                    TextKeywordExtractionRequest,
                    IReadOnlyList<TextKeyword>>(
                    [keywordProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var agent =
            new LongTextInsightAgent(
                segmentationExecutor,
                summarizationExecutor,
                keywordExecutor);

        var agentExecutor =
            new IntelligenceAgentExecutor<
                LongTextInsightRequest,
                TextInsight>(
                new IntelligenceAgentRegistry<
                    LongTextInsightRequest,
                    TextInsight>(
                    [agent]),
                auditSink);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "long-text-insight-operation-001"),
                classificationId,
                [
                    new ArtifactId(
                        "artifact-001")
                ],
                agent.Id);

        var result =
            await agentExecutor.ExecuteAsync(
                agent.Id,
                new LongTextInsightRequest(
                    "alpha beta alpha gamma",
                    11,
                    0,
                    7,
                    3,
                    4),
                context);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "alpha…",
                "alpha…"),
            result.Output!.Summary);

        Assert.Equal(
            ["alpha", "beta", "gamma"],
            result.Output.Keywords
                .Select(keyword => keyword.Term)
                .ToArray());

        Assert.Equal(
            4,
            result.CapabilityExecutions.Count);

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

        Assert.Equal(3, result.Warnings.Count);
        Assert.True(result.RequiresReview);
    }
}
