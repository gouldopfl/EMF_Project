using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Tests.TestInfrastructure;

public sealed class InMemoryWorkflowRepository : IWorkflowRepository
{
    private readonly List<WorkflowCheckpoint> _checkpoints = new();
    private readonly Dictionary<WorkflowId, WorkflowExecutionRecord> _executions = new();


    public Task CreateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
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
}
