using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Orchestration.Contracts;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceDevelopmentWorkflowCoordinator
{
    private readonly IWorkflowService _workflowService;
    private readonly IEvidenceDevelopmentPlanRepository _repository;

    public EvidenceDevelopmentWorkflowCoordinator(
        IWorkflowService workflowService,
        IEvidenceDevelopmentPlanRepository repository)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(repository);

        _workflowService = workflowService;
        _repository = repository;
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

        return execution;
    }
}
