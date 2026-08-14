using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Intelligence.Agents;

internal sealed class
    SegmentedTextSummarizationCoordinator
{
    private readonly IIntelligenceCapabilityExecutor<
        TextSegmentationRequest,
        IReadOnlyList<TextSegment>>
        _segmentationExecutor;

    private readonly IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string> _summarizationExecutor;

    public SegmentedTextSummarizationCoordinator(
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

    public async Task<
        SegmentedTextSummarizationResult>
        ExecuteAsync(
            LongTextSummarizationRequest objective,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var executions =
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

        executions.Add(
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
                executions,
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
                executions,
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

            executions.Add(summaryResult.Metadata);

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
                    executions,
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
            executions,
            sourceArtifactIds,
            warnings,
            requiresReview);
    }

    private static
        SegmentedTextSummarizationResult
        CreateResult(
            bool success,
            string? message,
            string? output,
            IReadOnlyList<
                IntelligenceExecutionMetadata>
                executions,
            IReadOnlyCollection<ArtifactId>
                sourceArtifactIds,
            IReadOnlyList<string> warnings,
            bool requiresReview)
    {
        return new SegmentedTextSummarizationResult
        {
            Success = success,
            Message = message,
            Output = output,
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
