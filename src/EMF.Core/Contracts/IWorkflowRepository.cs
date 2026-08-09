using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Core.Contracts;

public interface IWorkflowRepository
{

    Task CreateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default);
    Task UpdateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default);


    Task<WorkflowExecutionRecord?> GetExecutionAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);

    Task AddCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);


    Task AddStatusTransitionAsync(
        WorkflowStatusTransition transition,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStatusTransition>> GetStatusTransitionsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);

}
