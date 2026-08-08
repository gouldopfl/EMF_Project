using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class WorkflowService : IWorkflowService
{
    private readonly IWorkflowRepository _repository;

    public WorkflowService(
        IWorkflowRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public async Task<WorkflowId> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var workflowId =
            new WorkflowId(Guid.NewGuid().ToString());

        await _repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "Workflow Started",
                Status = WorkflowStatus.Running,
                RecordedUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return workflowId;
    }

    public Task RecordCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return _repository.AddCheckpointAsync(
            checkpoint,
            cancellationToken);
    }

    public Task CompleteAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        return _repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "Workflow Completed",
                Status = WorkflowStatus.Completed,
                RecordedUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    public Task FailAsync(
        WorkflowId workflowId,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return _repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "Workflow Failed",
                Status = WorkflowStatus.Failed,
                RecordedUtc = DateTimeOffset.UtcNow,
                Message = message
            },
            cancellationToken);
    }
}
