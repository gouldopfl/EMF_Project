using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class MedicalLiteratureClassificationCoordinator :
    IMedicalLiteratureClassificationCoordinator
{
    private readonly IMedicalLiteratureRepository _literature;
    private readonly IRegulatoryRepository _regulatory;
    private readonly IArtifactTextExtractor _textExtractor;
    private readonly MedicalLiteratureClassificationService _service;

    public MedicalLiteratureClassificationCoordinator(
        IMedicalLiteratureRepository literature,
        IRegulatoryRepository regulatory,
        IArtifactTextExtractor textExtractor,
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(literature);
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(executor);

        _literature = literature;
        _regulatory = regulatory;
        _textExtractor = textExtractor;
        _service = new MedicalLiteratureClassificationService(executor);
    }

    public async Task<MedicalLiteratureClassificationResult>
        ClassifyAsync(
            MedicalLiteratureSourceId sourceId,
            ArtifactId artifactId,
            IReadOnlyList<RequirementId> candidateRequirementIds,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateRequirementIds);
        ArgumentNullException.ThrowIfNull(context);

        if (candidateRequirementIds.Count !=
            candidateRequirementIds.Distinct().Count())
        {
            throw new InvalidOperationException(
                "Candidate requirements contain duplicate identities.");
        }

        var source =
            await _literature.GetMedicalLiteratureSourceAsync(
                sourceId,
                cancellationToken);

        if (source is null)
            throw new InvalidOperationException(
                $"Medical literature source not found: {sourceId.Value}");

        if (source.Id != sourceId)
            throw new InvalidOperationException(
                "Medical literature source identity mismatch.");

        var artifactIds =
            await _literature.GetArtifactIdsAsync(
                sourceId,
                cancellationToken);

        if (!artifactIds.Contains(artifactId))
            throw new InvalidOperationException(
                "Artifact is not associated with the medical literature source.");

        var requirements =
            new List<
                EMF.Extensions.VeteransClaims.Regulatory.Requirement>(
                candidateRequirementIds.Count);

        foreach (var requirementId in candidateRequirementIds)
        {
            var requirement =
                await _regulatory.GetRequirementAsync(
                    requirementId,
                    cancellationToken);

            if (requirement is null)
                throw new InvalidOperationException(
                    $"Requirement not found: {requirementId.Value}");

            if (requirement.Id != requirementId)
                throw new InvalidOperationException(
                    "Regulatory requirement identity mismatch.");

            requirements.Add(requirement);
        }

        var text =
            await _textExtractor.ExtractTextAsync(
                artifactId,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                "Medical literature text could not be extracted.");

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

        return await _service.ClassifyAsync(
            source,
            artifactId,
            text,
            requirements,
            intelligenceContext,
            cancellationToken);
    }
}
