using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class DevelopEvidenceGapWorkflowActivity :
    IWorkflowActivity
{
    private readonly IEvidenceGapRepository _repository;
    private readonly IEvidenceRequirementGuidanceRepository _guidanceRepository;
    private readonly IEvidenceDevelopmentPlanRepository _developmentRepository;
    private readonly EvidenceGapId _evidenceGapId;

    public DevelopEvidenceGapWorkflowActivity(
        IEvidenceGapRepository repository,
        IEvidenceRequirementGuidanceRepository guidanceRepository,
        IEvidenceDevelopmentPlanRepository developmentRepository,
        EvidenceGapId evidenceGapId)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);
        ArgumentNullException.ThrowIfNull(developmentRepository);

        _repository = repository;
        _guidanceRepository = guidanceRepository;
        _developmentRepository = developmentRepository;
        _evidenceGapId = evidenceGapId;
    }

    public string Id => "develop-evidence-gap";

    public string Name => "Develop Evidence Gap";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var gap =
            await _repository.GetEvidenceGapAsync(
                _evidenceGapId,
                cancellationToken);

        if (gap is null)
        {
            return new WorkflowActivityResult
            {
                Succeeded = false,
                Message = "Evidence gap was not found.",
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }

        var guidance =
            await _guidanceRepository
                .GetEvidenceRequirementGuidanceAsync(
                    gap.RequirementId,
                    cancellationToken);

        var developmentResult =
            new EMF.Extensions.VeteransClaims.Models.Adjudication.EvidenceDevelopmentResult
            {
                EvidenceGapId = gap.Id,
                RequirementId = gap.RequirementId,
                EvidenceGuidance = guidance
            };

        await _developmentRepository
            .AddEvidenceDevelopmentResultAsync(
                developmentResult,
                cancellationToken);

        return new WorkflowActivityResult
        {
            Succeeded = true,
            Message =
                $"Evidence gap: {gap.Description}; " +
                $"guidance items: {guidance.Count}.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
