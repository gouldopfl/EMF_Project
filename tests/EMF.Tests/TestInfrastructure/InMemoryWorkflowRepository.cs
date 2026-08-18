using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Tests.TestInfrastructure;

public sealed class InMemoryWorkflowRepository : IWorkflowRepository
{
    private readonly List<WorkflowCheckpoint> _checkpoints = new();
private readonly List<WorkflowStatusTransition> _transitions = new();
    private readonly Dictionary<WorkflowId, WorkflowExecutionRecord> _executions = new();


    public Task CreateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        _executions[execution.WorkflowId] = execution;
        return Task.CompletedTask;
    }

    public Task UpdateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        _executions[execution.WorkflowId] = execution;
        return Task.CompletedTask;
    }

    public Task<WorkflowExecutionRecord?> GetExecutionAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        _executions.TryGetValue(
            workflowId,
            out var execution);

        return Task.FromResult(execution);
    }

    public Task<WorkflowOperationRecord?> GetOperationAsync(
        WorkflowId workflowId,
        string activityId,
        OperationId operationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<WorkflowOperationRecord?>(null);
    }

    public Task<bool> TryCreateOperationAsync(
        WorkflowOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task UpdateOperationAsync(
        WorkflowOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowOperationRecord>> GetOperationsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<WorkflowOperationRecord>>(
            Array.Empty<WorkflowOperationRecord>());
    }

    public Task AddCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        _checkpoints.Add(checkpoint);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        var results = _checkpoints
            .Where(x => x.WorkflowId == workflowId)
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkflowCheckpoint>>(results);
    }


    public Task AddStatusTransitionAsync(
        WorkflowStatusTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        _transitions.Add(transition);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowStatusTransition>> GetStatusTransitionsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        var results = _transitions
            .Where(x => x.WorkflowId == workflowId)
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkflowStatusTransition>>(results);
    }


    public Task ApplyStatusTransitionAsync(
        WorkflowExecutionRecord execution,
        WorkflowStatusTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(transition);

        if (execution.WorkflowId != transition.WorkflowId)
        {
            throw new ArgumentException(
                "Execution and transition must reference the same workflow.");
        }

        _executions[execution.WorkflowId] = execution;
        _transitions.Add(transition);

        return Task.CompletedTask;
    }

}
