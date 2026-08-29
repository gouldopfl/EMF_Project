using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class VaDecisionDocumentInterpretationCoordinator :
    IVaDecisionDocumentInterpretationCoordinator
{
    private readonly IArtifactTextExtractor _textExtractor;
    private readonly VaDecisionDocumentInterpretationService _service;

    public VaDecisionDocumentInterpretationCoordinator(
        IArtifactTextExtractor textExtractor,
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(executor);

        _textExtractor = textExtractor;
        _service =
            new VaDecisionDocumentInterpretationService(
                executor);
    }

    public async Task<VaDecisionDocumentInterpretationResult>
        InterpretAsync(
            ArtifactId artifactId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var text =
            await _textExtractor.ExtractTextAsync(
                artifactId,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "VA decision document text could not be extracted.");
        }

        var inputArtifactIds =
            context.InputArtifactIds
                .Append(artifactId)
                .Distinct()
                .ToArray();

        var intelligenceContext =
            new IntelligenceExecutionContext(
                context.SubjectId,
                context.CorrelationId,
                context.ProtectionClassificationId,
                inputArtifactIds,
                context.AgentId);

        return await _service.InterpretAsync(
            artifactId,
            text,
            intelligenceContext,
            cancellationToken);
    }
}
