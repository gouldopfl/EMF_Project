using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceDevelopmentIntelligenceService
{
    private readonly
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> _summarizationExecutor;

    public EvidenceDevelopmentIntelligenceService(
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> summarizationExecutor)
    {
        ArgumentNullException.ThrowIfNull(
            summarizationExecutor);

        _summarizationExecutor =
            summarizationExecutor;
    }

    public async Task<EvidenceDevelopmentIntelligenceResult>
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

        var result =
            await _summarizationExecutor.ExecuteAsync(
                IntelligenceCapabilityIds.TextSummarization,
                new TextSummarizationRequest(
                    text,
                    1000),
                context,
                cancellationToken);

        return new EvidenceDevelopmentIntelligenceResult
        {
            Succeeded =
                result.Success &&
                !string.IsNullOrWhiteSpace(result.Output),

            Summary = result.Output,
            Message = result.Message,
            RequiresReview = result.RequiresReview,
            Metadata = result.Metadata
        };
    }

    private static string BuildInput(
        EvidenceGap gap,
        IReadOnlyList<EvidenceRequirementGuidance> guidance)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Summarize the following evidence-development gap.");
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
