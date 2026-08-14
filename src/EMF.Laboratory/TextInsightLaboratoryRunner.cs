using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Laboratory;

public sealed class TextInsightLaboratoryRunner
{
    private readonly IIntelligenceAgentExecutor<
        LongTextInsightRequest,
        TextInsight> _agentExecutor;

    public TextInsightLaboratoryRunner(
        IIntelligenceAgentExecutor<
            LongTextInsightRequest,
            TextInsight> agentExecutor)
    {
        ArgumentNullException.ThrowIfNull(
            agentExecutor);

        _agentExecutor = agentExecutor;
    }

    public Task<IntelligenceAgentResult<TextInsight>>
        RunAsync(
            string text,
            string subjectId,
            IntelligenceCorrelationId correlationId,
            ProtectionClassificationId
                protectionClassificationId,
            IReadOnlyCollection<ArtifactId>
                inputArtifactIds,
            TextInsightLaboratoryOptions? options = null,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(
            inputArtifactIds);

        options ??=
            new TextInsightLaboratoryOptions();

        var context =
            new IntelligenceExecutionContext(
                subjectId,
                correlationId,
                protectionClassificationId,
                inputArtifactIds,
                IntelligenceAgentIds.LongTextInsight);

        var objective =
            new LongTextInsightRequest(
                text,
                options.MaximumSegmentCharacters,
                options.OverlapCharacters,
                options.MaximumSummaryCharacters,
                options.MaximumKeywords,
                options.MinimumKeywordLength,
                options.ExcludedTerms);

        return _agentExecutor.ExecuteAsync(
            IntelligenceAgentIds.LongTextInsight,
            objective,
            context,
            cancellationToken);
    }
}
