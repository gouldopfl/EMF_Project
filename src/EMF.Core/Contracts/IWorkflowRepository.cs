using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Core.Contracts;

public interface IWorkflowRepository
{
    Task AddCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);
}
