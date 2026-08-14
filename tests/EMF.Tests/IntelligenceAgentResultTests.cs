using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceAgentResultTests
{
    [Fact]
    public void Result_ExposesCoordinatedCapabilityFacts()
    {
        var startedUtc =
            new DateTimeOffset(
                2026, 8, 14, 13, 0, 0,
                TimeSpan.Zero);

        var capabilityExecution =
            new IntelligenceExecutionMetadata
            {
                CapabilityId =
                    new IntelligenceCapabilityId(
                        "document-analysis"),
                ProviderId =
                    new IntelligenceProviderId(
                        "provider-one"),
                CorrelationId =
                    new IntelligenceCorrelationId(
                        "operation-001"),
                EngineName = "test-engine",
                StartedUtc = startedUtc,
                CompletedUtc =
                    startedUtc.AddSeconds(2)
            };

        var result =
            new IntelligenceAgentResult<string>
            {
                Success = true,
                Output = "coordinated-result",
                AgentId =
                    new AgentId(
                        "evidence-review-agent"),
                CorrelationId =
                    new IntelligenceCorrelationId(
                        "operation-001"),
                StartedUtc = startedUtc,
                CompletedUtc =
                    startedUtc.AddSeconds(3),
                CapabilityExecutions =
                    [capabilityExecution],
                SourceArtifactIds =
                    [new ArtifactId("artifact-001")],
                RequiresReview = true
            };

        IOperationResult operationResult = result;

        Assert.True(operationResult.Success);
        Assert.Equal(
            "coordinated-result",
            result.Output);

        Assert.Equal(
            "evidence-review-agent",
            result.AgentId.Value);

        Assert.Same(
            capabilityExecution,
            Assert.Single(
                result.CapabilityExecutions));

        Assert.Equal(
            "artifact-001",
            Assert.Single(
                result.SourceArtifactIds).Value);

        Assert.True(result.RequiresReview);
    }
}
