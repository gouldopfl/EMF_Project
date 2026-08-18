using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowRecoveryCoordinator
{
    Task<WorkflowRecoveryResult> RecoverAsync(
        WorkflowId workflowId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);
}
