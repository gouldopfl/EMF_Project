using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class TextInsightExecutionServiceTests
{
    [Fact]
    public void Options_ExposeSafeLocalDefaults()
    {
        var options =
            new TextInsightExecutionOptions();

        Assert.Equal(
            4_000,
            options.MaximumSegmentCharacters);
        Assert.Equal(
            200,
            options.OverlapCharacters);
        Assert.Equal(
            1_000,
            options.MaximumSummaryCharacters);
        Assert.Equal(20, options.MaximumKeywords);
        Assert.Equal(
            4,
            options.MinimumKeywordLength);
    }

    [Fact]
    public async Task RunAsync_CreatesLongTextInsightOperation()
    {
        var executor =
            new RecordingAgentExecutor();

        var runner =
            new TextInsightExecutionService(
                executor);

        var artifactId =
            new ArtifactId("artifact-001");

        var result =
            await runner.RunAsync(
                "Source document.",
                "laboratory-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [artifactId],
                new TextInsightExecutionOptions(
                    1_000,
                    100,
                    200,
                    10,
                    5,
                    ["custom"]));

        Assert.True(result.Success);

        Assert.Equal(
            IntelligenceAgentIds.LongTextInsight,
            executor.AgentId);

        Assert.NotNull(executor.Objective);
        Assert.NotNull(executor.Context);

        Assert.Equal(
            "Source document.",
            executor.Objective!.Text);
        Assert.Equal(
            1_000,
            executor.Objective
                .MaximumSegmentCharacters);
        Assert.Equal(
            ["custom"],
            executor.Objective.ExcludedTerms);

        Assert.Equal(
            "laboratory-steward",
            executor.Context!.SubjectId);
        Assert.Equal(
            IntelligenceAgentIds.LongTextInsight,
            executor.Context.AgentId);
        Assert.Equal(
            artifactId,
            Assert.Single(
                executor.Context.InputArtifactIds));
    }

    private sealed class RecordingAgentExecutor :
        IIntelligenceAgentExecutor<
            LongTextInsightRequest,
            TextInsight>
    {
        public AgentId AgentId { get; private set; }

        public LongTextInsightRequest? Objective
        { get; private set; }

        public IntelligenceExecutionContext? Context
        { get; private set; }

        public Task<
            IntelligenceAgentResult<TextInsight>>
            ExecuteAsync(
                AgentId agentId,
                LongTextInsightRequest objective,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            AgentId = agentId;
            Objective = objective;
            Context = context;

            var occurredUtc = DateTimeOffset.UtcNow;

            return Task.FromResult(
                new IntelligenceAgentResult<TextInsight>
                {
                    Success = true,
                    Output =
                        new TextInsight(
                            "summary",
                            Array.Empty<TextKeyword>()),
                    AgentId = agentId,
                    CorrelationId =
                        context.CorrelationId,
                    StartedUtc = occurredUtc,
                    CompletedUtc = occurredUtc
                });
        }
    }
}
