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
    private readonly
        SegmentedTextSummarizationCoordinator
        _coordinator;

    public LongTextSummarizationAgent(
        IIntelligenceCapabilityExecutor<
            TextSegmentationRequest,
            IReadOnlyList<TextSegment>>
            segmentationExecutor,
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> summarizationExecutor)
    {
        _coordinator =
            new SegmentedTextSummarizationCoordinator(
                segmentationExecutor,
                summarizationExecutor);
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

        var result =
            await _coordinator.ExecuteAsync(
                objective,
                context,
                cancellationToken);

        return new IntelligenceAgentResult<string>
        {
            Success = result.Success,
            Message = result.Message,
            Output = result.Output,
            AgentId = Id,
            CorrelationId = context.CorrelationId,
            StartedUtc = startedUtc,
            CompletedUtc = DateTimeOffset.UtcNow,
            CapabilityExecutions =
                result.CapabilityExecutions,
            SourceArtifactIds =
                result.SourceArtifactIds,
            Warnings = result.Warnings,
            RequiresReview =
                result.RequiresReview
        };
    }
}
