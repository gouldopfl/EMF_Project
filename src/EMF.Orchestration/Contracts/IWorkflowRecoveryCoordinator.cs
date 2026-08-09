using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowRecoveryCoordinator
{
    Task<RecoveryDecision> RecoverAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);
}
