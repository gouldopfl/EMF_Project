using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowService
{
    Task<WorkflowId> StartAsync(
        CancellationToken cancellationToken = default);

    Task RecordCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        WorkflowId workflowId,
        string message,
        CancellationToken cancellationToken = default);
}
