using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class LongTextSummarizationAgent :
    IIntelligenceAgent<
        LongTextSummarizationRequest,
        string>
{
    private readonly IIntelligenceCapabilityExecutor<
        TextSegmentationRequest,
        IReadOnlyList<TextSegment>>
        _segmentationExecutor;

    private readonly IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string> _summarizationExecutor;

    public LongTextSummarizationAgent(
        IIntelligenceCapabilityExecutor<
            TextSegmentationRequest,
            IReadOnlyList<TextSegment>>
            segmentationExecutor,
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> summarizationExecutor)
    {
        ArgumentNullException.ThrowIfNull(
            segmentationExecutor);
        ArgumentNullException.ThrowIfNull(
            summarizationExecutor);

        _segmentationExecutor =
            segmentationExecutor;
        _summarizationExecutor =
            summarizationExecutor;
    }

    public AgentId Id =>
        IntelligenceAgentIds.LongTextSummarization;

    public async Task<
        IntelligenceAgentResult<string>>
        ExecuteAsync(
            LongTextSummarizationRequest objective,
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
                "match the long-text summarization agent.",
                nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var capabilityExecutions =
            new List<IntelligenceExecutionMetadata>();

        var sourceArtifactIds =
            context.InputArtifactIds.ToHashSet();

        var warnings = new List<string>();
        var requiresReview = false;

        var segmentationResult =
            await _segmentationExecutor.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextSegmentation,
                new TextSegmentationRequest(
                    objective.Text,
                    objective.MaximumSegmentCharacters,
                    objective.OverlapCharacters),
                context,
                cancellationToken);

        capabilityExecutions.Add(
            segmentationResult.Metadata);

        sourceArtifactIds.UnionWith(
            segmentationResult.SourceArtifactIds);

        warnings.AddRange(
            segmentationResult.Warnings);

        requiresReview |=
            segmentationResult.RequiresReview;

        if (!segmentationResult.Success ||
            segmentationResult.Output is null)
        {
            return CreateResult(
                false,
                segmentationResult.Message,
                null,
                startedUtc,
                context,
                capabilityExecutions,
                sourceArtifactIds,
                warnings,
                requiresReview);
        }

        if (segmentationResult.Output.Count == 0)
        {
            return CreateResult(
                false,
                "Segmentation produced no text segments.",
                null,
                startedUtc,
                context,
                capabilityExecutions,
                sourceArtifactIds,
                warnings,
                requiresReview);
        }

        var summaries = new List<string>();

        foreach (var segment in
            segmentationResult.Output)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var summaryResult =
                await _summarizationExecutor.ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextSummarization,
                    new TextSummarizationRequest(
                        segment.Text,
                        objective
                            .MaximumSummaryCharacters),
                    context,
                    cancellationToken);

            capabilityExecutions.Add(
                summaryResult.Metadata);

            sourceArtifactIds.UnionWith(
                summaryResult.SourceArtifactIds);

            warnings.AddRange(
                summaryResult.Warnings);

            requiresReview |=
                summaryResult.RequiresReview;

            if (!summaryResult.Success ||
                summaryResult.Output is null)
            {
                return CreateResult(
                    false,
                    summaryResult.Message,
                    null,
                    startedUtc,
                    context,
                    capabilityExecutions,
                    sourceArtifactIds,
                    warnings,
                    requiresReview);
            }

            summaries.Add(summaryResult.Output);
        }

        return CreateResult(
            true,
            $"Summarized {summaries.Count} text segments.",
            string.Join(
                Environment.NewLine,
                summaries),
            startedUtc,
            context,
            capabilityExecutions,
            sourceArtifactIds,
            warnings,
            requiresReview);
    }

    private static IntelligenceAgentResult<string>
        CreateResult(
            bool success,
            string? message,
            string? output,
            DateTimeOffset startedUtc,
            IntelligenceExecutionContext context,
            IReadOnlyList<
                IntelligenceExecutionMetadata>
                capabilityExecutions,
            IReadOnlyCollection<ArtifactId>
                sourceArtifactIds,
            IReadOnlyList<string> warnings,
            bool requiresReview)
    {
        return new IntelligenceAgentResult<string>
        {
            Success = success,
            Message = message,
            Output = output,
            AgentId =
                IntelligenceAgentIds
                    .LongTextSummarization,
            CorrelationId = context.CorrelationId,
            StartedUtc = startedUtc,
            CompletedUtc = DateTimeOffset.UtcNow,
            CapabilityExecutions =
                capabilityExecutions.ToArray(),
            SourceArtifactIds =
                sourceArtifactIds.ToArray(),
            Warnings =
                warnings.Distinct().ToArray(),
            RequiresReview = requiresReview
        };
    }
}
