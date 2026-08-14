using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class TextSummarizationAgentTests
{
    [Fact]
    public async Task ExecuteAsync_CoordinatesSummarizationCapability()
    {
        var artifactId =
            new ArtifactId(
                "artifact-001");

        var metadata =
            new IntelligenceExecutionMetadata
            {
                CapabilityId =
                    IntelligenceCapabilityIds
                        .TextSummarization,
                ProviderId =
                    new IntelligenceProviderId(
                        "development.local"),
                CorrelationId =
                    new IntelligenceCorrelationId(
                        "operation-001"),
                EngineName =
                    "deterministic-extractive",
                StartedUtc =
                    DateTimeOffset.UtcNow,
                CompletedUtc =
                    DateTimeOffset.UtcNow
            };

        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output = "summary",
                Metadata = metadata,
                SourceArtifactIds = [artifactId],
                Warnings = ["Review required."],
                RequiresReview = true
            };

        var capabilityExecutor =
            new RecordingCapabilityExecutor(
                capabilityResult);

        var agent =
            new TextSummarizationAgent(
                capabilityExecutor);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [artifactId],
                agent.Id);

        var objective =
            new TextSummarizationRequest(
                "Source document.",
                100);

        var result =
            await agent.ExecuteAsync(
                objective,
                context);

        Assert.Equal("summary", result.Output);
        Assert.Equal(agent.Id, result.AgentId);

        Assert.Same(
            metadata,
            Assert.Single(
                result.CapabilityExecutions));

        Assert.Equal(
            artifactId,
            Assert.Single(
                result.SourceArtifactIds));

        Assert.Single(result.Warnings);
        Assert.True(result.RequiresReview);

        Assert.Equal(
            IntelligenceCapabilityIds
                .TextSummarization,
            capabilityExecutor.CapabilityId);

        Assert.Same(
            objective,
            capabilityExecutor.Request);

        Assert.Same(
            context,
            capabilityExecutor.Context);
    }

    private sealed class RecordingCapabilityExecutor :
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string>
    {
        private readonly
            IntelligenceCapabilityResult<string>
            _result;

        public RecordingCapabilityExecutor(
            IntelligenceCapabilityResult<string>
                result)
        {
            _result = result;
        }

        public IntelligenceCapabilityId CapabilityId
        { get; private set; }

        public TextSummarizationRequest? Request
        { get; private set; }

        public IntelligenceExecutionContext? Context
        { get; private set; }

        public Task<
            IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextSummarizationRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            CapabilityId = capabilityId;
            Request = request;
            Context = context;

            return Task.FromResult(_result);
        }
    }
}
