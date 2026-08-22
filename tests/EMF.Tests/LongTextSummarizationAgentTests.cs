using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class LongTextSummarizationAgentTests
{
    [Fact]
    public async Task ExecuteAsync_SegmentsAndSummarizesText()
    {
        var artifactId =
            new ArtifactId("artifact-001");

        var segmentationExecutor =
            new StubCapabilityExecutor<
                TextSegmentationRequest,
                IReadOnlyList<TextSegment>>(
                request =>
                    new IntelligenceCapabilityResult<
                        IReadOnlyList<TextSegment>>
                    {
                        Success = true,
                        Output =
                        [
                            new TextSegment(
                                0,
                                0,
                                "First segment"),
                            new TextSegment(
                                1,
                                10,
                                "Second segment")
                        ],
                        Metadata =
                            CreateMetadata(
                                IntelligenceCapabilityIds
                                    .TextSegmentation),
                        SourceArtifactIds = [artifactId],
                        Warnings =
                            ["Fixed boundaries."]
                    });

        var summaryNumber = 0;

        var summarizationExecutor =
            new StubCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                request =>
                {
                    summaryNumber++;

                    return new
                        IntelligenceCapabilityResult<string>
                    {
                        Success = true,
                        Output =
                            $"summary-{summaryNumber}",
                        Metadata =
                            CreateMetadata(
                                IntelligenceCapabilityIds
                                    .TextSummarization),
                        SourceArtifactIds = [artifactId],
                        Warnings =
                            ["Review summary."],
                        RequiresReview = true
                    };
                });

        var agent =
            new LongTextSummarizationAgent(
                segmentationExecutor,
                summarizationExecutor);

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
            new LongTextSummarizationRequest(
                "First segment Second segment",
                13,
                3,
                50);

        var result =
            await agent.ExecuteAsync(
                objective,
                context);

        Assert.True(result.Success);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "summary-1",
                "summary-2"),
            result.Output);

        Assert.Equal(agent.Id, result.AgentId);
        Assert.Equal(
            3,
            result.CapabilityExecutions.Count);

        Assert.Equal(
            artifactId,
            Assert.Single(
                result.SourceArtifactIds));

        Assert.Equal(2, result.Warnings.Count);
        Assert.True(result.RequiresReview);

        var segmentationRequest =
            Assert.Single(
                segmentationExecutor.Requests);

        Assert.Equal(
            objective.Text,
            segmentationRequest.Text);
        Assert.Equal(
            objective.MaximumSegmentCharacters,
            segmentationRequest
                .MaximumSegmentCharacters);
        Assert.Equal(
            objective.OverlapCharacters,
            segmentationRequest
                .OverlapCharacters);

        Assert.Equal(
            ["First segment", "Second segment"],
            summarizationExecutor.Requests
                .Select(request => request.Text)
                .ToArray());

        Assert.All(
            summarizationExecutor.Requests,
            request => Assert.Equal(
                objective.MaximumSummaryCharacters,
                request.MaximumCharacters));
    }


    [Fact]
    public async Task ExecuteAsync_PreservesContextInputArtifactsAsSources()
    {
        var artifactId =
            new ArtifactId("artifact-context-long-001");

        var segmentationExecutor =
            new StubCapabilityExecutor<
                TextSegmentationRequest,
                IReadOnlyList<TextSegment>>(
                _ =>
                    new IntelligenceCapabilityResult<
                        IReadOnlyList<TextSegment>>
                    {
                        Success = true,
                        Output =
                        [
                            new TextSegment(
                                0,
                                0,
                                "Only segment")
                        ],
                        Metadata =
                            CreateMetadata(
                                IntelligenceCapabilityIds
                                    .TextSegmentation)
                    });

        var summarizationExecutor =
            new StubCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                _ =>
                    new IntelligenceCapabilityResult<string>
                    {
                        Success = true,
                        Output = "summary",
                        Metadata =
                            CreateMetadata(
                                IntelligenceCapabilityIds
                                    .TextSummarization)
                    });

        var agent =
            new LongTextSummarizationAgent(
                segmentationExecutor,
                summarizationExecutor);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-long-provenance-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [artifactId],
                agent.Id);

        var result =
            await agent.ExecuteAsync(
                new LongTextSummarizationRequest(
                    "Only segment",
                    100,
                    0,
                    50),
                context);

        Assert.Equal(
            artifactId,
            Assert.Single(
                result.SourceArtifactIds));
    }


    private static IntelligenceExecutionMetadata
        CreateMetadata(
            IntelligenceCapabilityId capabilityId)
    {
        var occurredUtc = DateTimeOffset.UtcNow;

        return new IntelligenceExecutionMetadata
        {
            CapabilityId = capabilityId,
            ProviderId =
                new IntelligenceProviderId(
                    "development.local"),
            CorrelationId =
                new IntelligenceCorrelationId(
                    "operation-001"),
            EngineName = "test-engine",
            StartedUtc = occurredUtc,
            CompletedUtc = occurredUtc
        };
    }

    private sealed class StubCapabilityExecutor<
        TRequest,
        TResult> :
        IIntelligenceCapabilityExecutor<
            TRequest,
            TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        private readonly Func<
            TRequest,
            IntelligenceCapabilityResult<TResult>>
            _resultFactory;

        public StubCapabilityExecutor(
            Func<
                TRequest,
                IntelligenceCapabilityResult<TResult>>
                resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public List<TRequest> Requests { get; } = [];

        public Task<
            IntelligenceCapabilityResult<TResult>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var expectedCapabilityId =
                typeof(TRequest) ==
                    typeof(TextSegmentationRequest)
                    ? IntelligenceCapabilityIds
                        .TextSegmentation
                    : IntelligenceCapabilityIds
                        .TextSummarization;

            Assert.Equal(
                expectedCapabilityId,
                capabilityId);

            Requests.Add(request);

            return Task.FromResult(
                _resultFactory(request));
        }
    }
}
