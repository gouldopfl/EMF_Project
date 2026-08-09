using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class WorkflowRecoveryCoordinator : IWorkflowRecoveryCoordinator
{
    private readonly IWorkflowRepository _repository;
    private readonly IWorkflowRecoveryPolicy _policy;

    public WorkflowRecoveryCoordinator(
        IWorkflowRepository repository,
        IWorkflowRecoveryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(policy);

        _repository = repository;
        _policy = policy;
    }

    public async Task<RecoveryDecision> RecoverAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        var execution =
            await _repository.GetExecutionAsync(
                workflowId,
                cancellationToken);

        if (execution is null)
        {
            return RecoveryDecision.Failed;
        }

        var checkpoints =
            await _repository.GetCheckpointsAsync(
                workflowId,
                cancellationToken);

        return await _policy.EvaluateAsync(
            execution,
            checkpoints,
            cancellationToken);
    }
}
