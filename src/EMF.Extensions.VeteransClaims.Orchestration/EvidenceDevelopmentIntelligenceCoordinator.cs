using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
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
    public async Task<EvidenceDevelopmentIntelligenceResult>
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

        return await _service.SummarizeAsync(
            gap,
            developmentResult.EvidenceGuidance,
            context,
            cancellationToken);
    }

}
