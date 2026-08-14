using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class LongTextInsightAgent :
    IIntelligenceAgent<
        LongTextInsightRequest,
        TextInsight>
{
    private readonly
        SegmentedTextSummarizationCoordinator
        _summarizationCoordinator;

    private readonly IIntelligenceCapabilityExecutor<
        TextKeywordExtractionRequest,
        IReadOnlyList<TextKeyword>>
        _keywordExecutor;

    public LongTextInsightAgent(
        IIntelligenceCapabilityExecutor<
            TextSegmentationRequest,
            IReadOnlyList<TextSegment>>
            segmentationExecutor,
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> summarizationExecutor,
        IIntelligenceCapabilityExecutor<
            TextKeywordExtractionRequest,
            IReadOnlyList<TextKeyword>>
            keywordExecutor)
    {
        _summarizationCoordinator =
            new SegmentedTextSummarizationCoordinator(
                segmentationExecutor,
                summarizationExecutor);

        ArgumentNullException.ThrowIfNull(
            keywordExecutor);

        _keywordExecutor = keywordExecutor;
    }

    public AgentId Id =>
        IntelligenceAgentIds.LongTextInsight;

    public async Task<
        IntelligenceAgentResult<TextInsight>>
        ExecuteAsync(
            LongTextInsightRequest objective,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.AgentId.HasValue ||
            context.AgentId.Value != Id)
        {
            throw new ArgumentException(
                "Execution context Agent ID must " +
                "match the long-text insight agent.",
                nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var summarizationResult =
            await _summarizationCoordinator
                .ExecuteAsync(
                    new LongTextSummarizationRequest(
                        objective.Text,
                        objective
                            .MaximumSegmentCharacters,
                        objective.OverlapCharacters,
                        objective
                            .MaximumSummaryCharacters),
                    context,
                    cancellationToken);

        if (!summarizationResult.Success ||
            summarizationResult.Output is null)
        {
            return CreateResult(
                false,
                summarizationResult.Message,
                null,
                startedUtc,
                context,
                summarizationResult
                    .CapabilityExecutions,
                summarizationResult
                    .SourceArtifactIds,
                summarizationResult.Warnings,
                summarizationResult.RequiresReview);
        }

        var executions =
            summarizationResult
                .CapabilityExecutions.ToList();

        var sourceArtifactIds =
            summarizationResult
                .SourceArtifactIds.ToHashSet();

        var warnings =
            summarizationResult.Warnings.ToList();

        var requiresReview =
            summarizationResult.RequiresReview;

        var keywordResult =
            await _keywordExecutor.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextKeywordExtraction,
                new TextKeywordExtractionRequest(
                    objective.Text,
                    objective.MaximumKeywords,
                    objective.MinimumKeywordLength,
                    objective.ExcludedTerms),
                context,
                cancellationToken);

        executions.Add(keywordResult.Metadata);

        sourceArtifactIds.UnionWith(
            keywordResult.SourceArtifactIds);

        warnings.AddRange(
            keywordResult.Warnings);

        requiresReview |=
            keywordResult.RequiresReview;

        if (!keywordResult.Success ||
            keywordResult.Output is null)
        {
            return CreateResult(
                false,
                keywordResult.Message,
                null,
                startedUtc,
                context,
                executions,
                sourceArtifactIds,
                warnings,
                requiresReview);
        }

        return CreateResult(
            true,
            "Long-text insight generated.",
            new TextInsight(
                summarizationResult.Output,
                keywordResult.Output),
            startedUtc,
            context,
            executions,
            sourceArtifactIds,
            warnings,
            requiresReview);
    }

    private static
        IntelligenceAgentResult<TextInsight>
        CreateResult(
            bool success,
            string? message,
            TextInsight? output,
            DateTimeOffset startedUtc,
            IntelligenceExecutionContext context,
            IReadOnlyList<
                IntelligenceExecutionMetadata>
                executions,
            IReadOnlyCollection<ArtifactId>
                sourceArtifactIds,
            IReadOnlyList<string> warnings,
            bool requiresReview)
    {
        return new IntelligenceAgentResult<TextInsight>
        {
            Success = success,
            Message = message,
            Output = output,
            AgentId =
                IntelligenceAgentIds.LongTextInsight,
            CorrelationId = context.CorrelationId,
            StartedUtc = startedUtc,
            CompletedUtc = DateTimeOffset.UtcNow,
            CapabilityExecutions =
                executions.ToArray(),
            SourceArtifactIds =
                sourceArtifactIds.ToArray(),
            Warnings =
                warnings.Distinct().ToArray(),
            RequiresReview = requiresReview
        };
    }
}
