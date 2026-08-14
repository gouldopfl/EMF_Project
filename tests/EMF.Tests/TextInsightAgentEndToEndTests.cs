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
    public async Task LocalStack_GeneratesTextInsight()
    {
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
                        summarizationProvider.ProviderId,
                        summarizationProvider.Id,
                        classificationId),
                    new IntelligenceProviderRoutingGrant(
                        keywordProvider.ProviderId,
                        keywordProvider.Id,
                        classificationId)
                ]);

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
            new TextInsightAgent(
                summarizationExecutor,
                keywordExecutor);

        var agentExecutor =
            new IntelligenceAgentExecutor<
                TextInsightRequest,
                TextInsight>(
                new IntelligenceAgentRegistry<
                    TextInsightRequest,
                    TextInsight>(
                    [agent]),
                auditSink);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "text-insight-operation-001"),
                classificationId,
                [
                    new ArtifactId(
                        "artifact-001")
                ],
                agent.Id);

        var result =
            await agentExecutor.ExecuteAsync(
                agent.Id,
                new TextInsightRequest(
                    "Evidence evidence policy.",
                    12,
                    2,
                    4),
                context);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);

        Assert.Equal(
            "Evidence ev…",
            result.Output!.Summary);

        Assert.Equal(
            ["evidence", "policy"],
            result.Output.Keywords
                .Select(keyword => keyword.Term)
                .ToArray());

        Assert.Equal(
            2,
            result.CapabilityExecutions.Count);

        Assert.Equal(
            2,
            authorizationPolicy.Requests.Count);

        Assert.Equal(
            2,
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
