using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class TextSummarizationAgent :
    IIntelligenceAgent<
        TextSummarizationRequest,
        string>
{
    private readonly IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string> _capabilityExecutor;

    public TextSummarizationAgent(
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> capabilityExecutor)
    {
        ArgumentNullException.ThrowIfNull(
            capabilityExecutor);

        _capabilityExecutor =
            capabilityExecutor;
    }

    public AgentId Id =>
        IntelligenceAgentIds.TextSummarization;

    public async Task<
        IntelligenceAgentResult<string>>
        ExecuteAsync(
            TextSummarizationRequest objective,
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
                "match the summarization agent.",
                nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var capabilityResult =
            await _capabilityExecutor.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextSummarization,
                objective,
                context,
                cancellationToken);

        var sourceArtifactIds =
            context.InputArtifactIds.ToHashSet();

        sourceArtifactIds.UnionWith(
            capabilityResult.SourceArtifactIds);

        return new IntelligenceAgentResult<string>
        {
            Success = capabilityResult.Success,
            Message = capabilityResult.Message,
            Output = capabilityResult.Output,
            AgentId = Id,
            CorrelationId =
                context.CorrelationId,
            StartedUtc = startedUtc,
            CompletedUtc =
                DateTimeOffset.UtcNow,
            CapabilityExecutions =
                [capabilityResult.Metadata],
            SourceArtifactIds =
                sourceArtifactIds.ToArray(),
            Warnings =
                capabilityResult.Warnings.ToArray(),
            RequiresReview =
                capabilityResult.RequiresReview
        };
    }
}
