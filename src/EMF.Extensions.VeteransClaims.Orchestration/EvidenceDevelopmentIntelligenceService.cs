using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class EvidenceDevelopmentIntelligenceService
{
    private readonly TextSummarizationAgent _agent;

    public EvidenceDevelopmentIntelligenceService(
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> summarizationExecutor)
    {
        ArgumentNullException.ThrowIfNull(
            summarizationExecutor);

        _agent =
            new TextSummarizationAgent(
                summarizationExecutor);
    }

    public Task<IntelligenceAgentResult<string>>
        SummarizeAsync(
            EvidenceGap gap,
            IReadOnlyList<EvidenceRequirementGuidance> guidance,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(context);

        var text = BuildInput(gap, guidance);

        var agentContext =
            new IntelligenceExecutionContext(
                context.SubjectId,
                context.CorrelationId,
                context.ProtectionClassificationId,
                context.InputArtifactIds,
                _agent.Id);

        return ExecuteAgentAsync(
            text,
            agentContext,
            cancellationToken);
    }

    private async Task<IntelligenceAgentResult<string>>
        ExecuteAgentAsync(
            string text,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken)
    {
        var result =
            await _agent.ExecuteAsync(
                new TextSummarizationRequest(
                    text,
                    1000),
                context,
                cancellationToken);

        if (!result.Success ||
            !string.IsNullOrWhiteSpace(result.Output))
        {
            return result;
        }

        return new IntelligenceAgentResult<string>
        {
            Success = false,
            Message = result.Message,
            Output = result.Output,
            AgentId = result.AgentId,
            CorrelationId = result.CorrelationId,
            StartedUtc = result.StartedUtc,
            CompletedUtc = result.CompletedUtc,
            CapabilityExecutions =
                result.CapabilityExecutions,
            SourceArtifactIds =
                result.SourceArtifactIds,
            Warnings = result.Warnings,
            RequiresReview = result.RequiresReview
        };
    }

    private static string BuildInput(
        EvidenceGap gap,
        IReadOnlyList<EvidenceRequirementGuidance> guidance)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Prepare a veteran-oriented evidence-development summary.");
        builder.AppendLine(
            "Identify what the evidence must establish, what evidence is needed, " +
            "and the specific types of documents or records the veteran should " +
            "look for or provide.");
        builder.AppendLine(
            "Explain why each identified evidence type is relevant.");
        builder.AppendLine(
            "Distinguish required evidence from supporting evidence when the " +
            "guidance permits that distinction.");
        builder.AppendLine(
            "Do not invent evidence requirements, document types, medical opinions, " +
            "forms, or other facts that are not supported by the supplied guidance.");
        builder.AppendLine(
            "This is evidence-development guidance, not an adjudication decision.");
        builder.AppendLine();
        builder.AppendLine(
            $"Gap: {gap.Description}");
        builder.AppendLine(
            $"Requirement ID: {gap.RequirementId.Value}");
        builder.AppendLine();
        builder.AppendLine("Evidence guidance:");

        foreach (var item in guidance)
        {
            builder.Append("- ");
            builder.Append(item.EvidenceClassification);
            builder.Append(" / ");
            builder.Append(item.GuidanceRole);
            builder.Append(": ");
            builder.AppendLine(item.Description);
        }

        return builder.ToString();
    }
}
