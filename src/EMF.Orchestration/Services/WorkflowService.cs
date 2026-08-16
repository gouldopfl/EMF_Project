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
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var workflowId =
            new WorkflowId(Guid.NewGuid().ToString());

        var createdUtc =
            DateTimeOffset.UtcNow;

        await _repository.CreateExecutionAsync(
            new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = definition.Id,
                DefinitionVersion = definition.Version,
                CreatedUtc = createdUtc,
                CurrentStatus = WorkflowStatus.Running,
                RecoveryStatus = WorkflowRecoveryStatus.None
            },
            cancellationToken);

        await _repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "Workflow Started",
                Status = WorkflowStatus.Running,
                RecordedUtc = createdUtc
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

    public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetCheckpointsAsync(
            workflowId,
            cancellationToken);
    }

    public async Task CompleteAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        var execution =
            await _repository.GetExecutionAsync(
                workflowId,
                cancellationToken);

        if (execution is null)
        {
            throw new InvalidOperationException(
                $"Workflow execution '{workflowId}' was not found.");
        }

        EnsureTransition(
            execution.CurrentStatus,
            WorkflowStatus.Completed);

        var completedUtc =
        DateTimeOffset.UtcNow;

    await _repository.ApplyStatusTransitionAsync(
        new WorkflowExecutionRecord
        {
            WorkflowId = execution.WorkflowId,
            DefinitionId = execution.DefinitionId,
            DefinitionVersion = execution.DefinitionVersion,
            CreatedUtc = execution.CreatedUtc,
            CurrentStatus = WorkflowStatus.Completed,
            RecoveryStatus = execution.RecoveryStatus,
            Revision = execution.Revision
        },
        new WorkflowStatusTransition
        {
            WorkflowId = execution.WorkflowId,
            FromStatus = execution.CurrentStatus,
            ToStatus = WorkflowStatus.Completed,
            RecordedUtc = completedUtc,
            Message = "Workflow completed"
        },
        cancellationToken);

        await _repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "Workflow Completed",
                Status = WorkflowStatus.Completed,
                RecordedUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    public async Task FailAsync(
        WorkflowId workflowId,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var execution =
            await _repository.GetExecutionAsync(
                workflowId,
                cancellationToken);

        if (execution is null)
        {
            throw new InvalidOperationException(
                $"Workflow execution '{workflowId}' was not found.");
        }

        EnsureTransition(
            execution.CurrentStatus,
            WorkflowStatus.Failed);

        var failedUtc =
        DateTimeOffset.UtcNow;

    await _repository.ApplyStatusTransitionAsync(
        new WorkflowExecutionRecord
        {
            WorkflowId = execution.WorkflowId,
            DefinitionId = execution.DefinitionId,
            DefinitionVersion = execution.DefinitionVersion,
            CreatedUtc = execution.CreatedUtc,
            CurrentStatus = WorkflowStatus.Failed,
            RecoveryStatus = execution.RecoveryStatus,
            Revision = execution.Revision
        },
        new WorkflowStatusTransition
        {
            WorkflowId = execution.WorkflowId,
            FromStatus = execution.CurrentStatus,
            ToStatus = WorkflowStatus.Failed,
            RecordedUtc = failedUtc,
            Message = message
        },
        cancellationToken);

        await _repository.AddCheckpointAsync(
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

    private static void EnsureTransition(
        WorkflowStatus current,
        WorkflowStatus next)
    {
        if (!WorkflowLifecycle.CanTransition(current, next))
        {
            throw new InvalidOperationException(
                $"Invalid workflow transition: {current} -> {next}.");
        }
    }
}
