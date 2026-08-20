using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Orchestration.Contracts;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceDevelopmentWorkflowCoordinator :
    IEvidenceDevelopmentWorkflowCoordinator
{
    private readonly IWorkflowService _workflowService;
    private readonly IEvidenceDevelopmentPlanRepository _repository;
    private readonly IWorkflowRunner _runner;
    private readonly IEvidenceGapRepository _gapRepository;
    private readonly IEvidenceRequirementGuidanceRepository _guidanceRepository;

    public EvidenceDevelopmentWorkflowCoordinator(
        IWorkflowService workflowService,
        IEvidenceDevelopmentPlanRepository repository,
        IWorkflowRunner runner,
        IEvidenceGapRepository gapRepository,
        IEvidenceRequirementGuidanceRepository guidanceRepository)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);

        _workflowService = workflowService;
        _repository = repository;
        _runner = runner;
        _gapRepository = gapRepository;
        _guidanceRepository = guidanceRepository;
    }

    public async Task<EvidenceDevelopmentExecution>
        StartAsync(
            EvidenceDevelopmentPlanId planId,
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
    {
        var workflowId =
            await _workflowService.StartAsync(
                EvidenceDevelopmentWorkflowDefinition.Create(),
                cancellationToken);

        var execution =
            new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId = planId,
                EvidenceGapId = evidenceGapId,
                WorkflowId = workflowId
            };

        try
        {
            await _repository.AddEvidenceDevelopmentExecutionAsync(
                execution,
                cancellationToken);
        }
        catch
        {
            await _workflowService.FailAsync(
                workflowId,
                "Evidence development workflow link persistence failed.",
                cancellationToken);

            throw;
        }

        await _runner.ExecuteAsync(
            new EMF.Orchestration.Models.WorkflowExecutionContext
            {
                WorkflowId = workflowId
            },
            new IWorkflowActivity[]
            {
                new DevelopEvidenceGapWorkflowActivity(
                    _gapRepository,
                    _guidanceRepository,
                    _repository,
                    evidenceGapId)
            },
            cancellationToken: cancellationToken);

        return execution;
    }
}
