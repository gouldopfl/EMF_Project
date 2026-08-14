using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class TextInsightAgent :
    IIntelligenceAgent<
        TextInsightRequest,
        TextInsight>
{
    private readonly IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string> _summarizationExecutor;

    private readonly IIntelligenceCapabilityExecutor<
        TextKeywordExtractionRequest,
        IReadOnlyList<TextKeyword>>
        _keywordExecutor;

    public TextInsightAgent(
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> summarizationExecutor,
        IIntelligenceCapabilityExecutor<
            TextKeywordExtractionRequest,
            IReadOnlyList<TextKeyword>>
            keywordExecutor)
    {
        ArgumentNullException.ThrowIfNull(
            summarizationExecutor);
        ArgumentNullException.ThrowIfNull(
            keywordExecutor);

        _summarizationExecutor =
            summarizationExecutor;
        _keywordExecutor = keywordExecutor;
    }

    public AgentId Id =>
        IntelligenceAgentIds.TextInsight;

    public async Task<
        IntelligenceAgentResult<TextInsight>>
        ExecuteAsync(
            TextInsightRequest objective,
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
                "match the text insight agent.",
                nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var executions =
            new List<IntelligenceExecutionMetadata>();

        var sourceArtifactIds =
            context.InputArtifactIds.ToHashSet();

        var warnings = new List<string>();
        var requiresReview = false;

        var summaryResult =
            await _summarizationExecutor.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextSummarization,
                new TextSummarizationRequest(
                    objective.Text,
                    objective
                        .MaximumSummaryCharacters),
                context,
                cancellationToken);

        executions.Add(summaryResult.Metadata);
        sourceArtifactIds.UnionWith(
            summaryResult.SourceArtifactIds);
        warnings.AddRange(summaryResult.Warnings);
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
                executions,
                sourceArtifactIds,
                warnings,
                requiresReview);
        }

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
        warnings.AddRange(keywordResult.Warnings);
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

        var insight =
            new TextInsight(
                summaryResult.Output,
                keywordResult.Output);

        return CreateResult(
            true,
            "Text insight generated.",
            insight,
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
                IntelligenceAgentIds.TextInsight,
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
