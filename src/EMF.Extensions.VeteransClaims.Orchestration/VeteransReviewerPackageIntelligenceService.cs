using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageIntelligenceService :
    IVeteransReviewerPackageIntelligenceService
{
    private readonly TextSummarizationAgent _agent;

    public VeteransReviewerPackageIntelligenceService(
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
            ClaimIssueAdjudicationDetails details,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(context);

        var source =
            VeteransReviewerPackageSourceFormatter.Format(
                details);

        var agentContext =
            new IntelligenceExecutionContext(
                context.SubjectId,
                context.CorrelationId,
                context.ProtectionClassificationId,
                context.InputArtifactIds,
                _agent.Id);

        return ExecuteAgentAsync(
            BuildInput(source),
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
                    2000),
                context,
                cancellationToken);

        var hasExpectedSourceArtifactIds =
            result.SourceArtifactIds is not null &&
            context.InputArtifactIds
                .ToHashSet()
                .SetEquals(
                    result.SourceArtifactIds);

        if (!result.Success ||
            (!string.IsNullOrWhiteSpace(result.Output) &&
             hasExpectedSourceArtifactIds))
        {
            return result;
        }

        return new IntelligenceAgentResult<string>
        {
            Success = false,
            Message =
                hasExpectedSourceArtifactIds
                    ? result.Message
                    : "Reviewer package summarization returned " +
                      "unexpected source artifact lineage.",
            Output = result.Output,
            AgentId = result.AgentId,
            CorrelationId = result.CorrelationId,
            StartedUtc = result.StartedUtc,
            CompletedUtc = result.CompletedUtc,
            CapabilityExecutions =
                result.CapabilityExecutions,
            SourceArtifactIds =
                result.SourceArtifactIds ?? [],
            Warnings = result.Warnings,
            RequiresReview = result.RequiresReview
        };
    }

    private static string BuildInput(string source)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Prepare a factual reviewer package summary.");

        builder.AppendLine(
            "Use only the facts supplied below.");

        builder.AppendLine(
            "Clearly separate evidence of record, outstanding evidence, " +
            "requirements, and procedural history.");

        builder.AppendLine(
            "Preserve citations and traceability identifiers when relevant.");

        builder.AppendLine(
            "Do not invent evidence, diagnoses, relationships, " +
            "requirements, or events.");

        builder.AppendLine(
            "Do not make medical, legal, or adjudicative conclusions.");

        builder.AppendLine(
            "This material organizes evidence for human review.");

        builder.AppendLine();
        builder.Append(source);

        return builder.ToString();
    }
}
