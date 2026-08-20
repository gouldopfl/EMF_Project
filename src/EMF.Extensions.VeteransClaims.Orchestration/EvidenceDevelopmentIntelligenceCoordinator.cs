using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceDevelopmentIntelligenceCoordinator
{
    private readonly IEvidenceDevelopmentPlanRepository _repository;
    private readonly IEvidenceGapRepository _gapRepository;
    private readonly EvidenceDevelopmentIntelligenceService _service;

    public EvidenceDevelopmentIntelligenceCoordinator(
        IEvidenceDevelopmentPlanRepository repository,
        IEvidenceGapRepository gapRepository,
        EvidenceDevelopmentIntelligenceService service)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(service);

        _repository = repository;
        _gapRepository = gapRepository;
        _service = service;
    }
    public async Task<IntelligenceAgentResult<string>>
        SummarizeAsync(
            EvidenceDevelopmentPlanId planId,
            EvidenceGapId evidenceGapId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var execution =
            await _repository.GetEvidenceDevelopmentExecutionAsync(
                planId,
                evidenceGapId,
                cancellationToken);

        if (execution is null)
        {
            throw new InvalidOperationException(
                "Evidence development execution was not found.");
        }

        var developmentResult =
            await _repository.GetEvidenceDevelopmentResultAsync(
                evidenceGapId,
                cancellationToken);

        if (developmentResult is null)
        {
            throw new InvalidOperationException(
                "Evidence development result was not found.");
        }

        var gap =
            await _gapRepository.GetEvidenceGapAsync(
                evidenceGapId,
                cancellationToken);

        if (gap is null)
        {
            throw new InvalidOperationException(
                "Evidence gap was not found.");
        }

        var gapArtifacts =
            await _gapRepository.GetEvidenceGapArtifactsAsync(
                evidenceGapId,
                cancellationToken);

        var inputArtifactIds =
            context.InputArtifactIds
                .Concat(
                    gapArtifacts.Select(
                        artifact => artifact.ArtifactId))
                .Distinct()
                .ToArray();

        var intelligenceContext =
            new IntelligenceExecutionContext(
                context.SubjectId,
                context.CorrelationId,
                context.ProtectionClassificationId,
                inputArtifactIds,
                context.AgentId);

        return await _service.SummarizeAsync(
            gap,
            developmentResult.EvidenceGuidance,
            intelligenceContext,
            cancellationToken);
    }

}
