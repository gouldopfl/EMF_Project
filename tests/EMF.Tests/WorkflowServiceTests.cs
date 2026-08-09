using EMF.Orchestration.Services;
using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Tests;

public sealed class WorkflowServiceTests
{
    [Fact]
    public async Task StartAsync_returns_workflow_id()
    {
        var service = new FakeWorkflowService();

        var workflowId =
            await service.StartAsync(CreateDefinition());

        Assert.False(
            string.IsNullOrWhiteSpace(
                workflowId.Value));
    }

    [Fact]
    public async Task RecordCheckpointAsync_stores_checkpoint()
    {
        var service = new FakeWorkflowService();

        var workflowId =
            await service.StartAsync(CreateDefinition());

        var checkpoint = new WorkflowCheckpoint
        {
            WorkflowId = workflowId,
            Step = "Discovery Complete",
            Status = WorkflowStatus.Completed,
            RecordedUtc = DateTimeOffset.UtcNow
        };

        await service.RecordCheckpointAsync(checkpoint);

        Assert.Single(service.Checkpoints);
    }

    private static WorkflowDefinition CreateDefinition()
    {
        return new WorkflowDefinition
        {
            Id = "evidence-processing",
            Name = "Evidence Processing",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };
    }

    private sealed class FakeWorkflowService : IWorkflowService
    {
        public List<WorkflowCheckpoint> Checkpoints { get; } = new();

        public Task<WorkflowId> StartAsync(
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definition);

            return Task.FromResult(
                new WorkflowId(Guid.NewGuid().ToString()));
        }

        public Task RecordCheckpointAsync(
            WorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            Checkpoints.Add(checkpoint);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowCheckpoint>>(
                Array.Empty<WorkflowCheckpoint>());
        }

        public Task CompleteAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task FailAsync(
            WorkflowId workflowId,
            string message,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

public sealed class WorkflowServicePersistenceTests
{
    [Fact]
    public async Task StartAsync_persists_workflow_definition_identity()
    {
        var repository = new RecordingWorkflowRepository();
        var service = new WorkflowService(repository);

        var definition = new WorkflowDefinition
        {
            Id = "evidence-processing",
            Name = "Evidence Processing",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var workflowId =
            await service.StartAsync(definition);

        Assert.NotNull(repository.Execution);
        Assert.Equal(workflowId, repository.Execution!.WorkflowId);
        Assert.Equal("evidence-processing", repository.Execution.DefinitionId);
        Assert.Equal("1", repository.Execution.DefinitionVersion);
        Assert.Equal(WorkflowStatus.Running, repository.Execution.CurrentStatus);
    }

    [Fact]
    public async Task CompleteAsync_updates_execution_status()
    {
        var repository = new RecordingWorkflowRepository();
        var service = new WorkflowService(repository);

        var definition = new WorkflowDefinition
        {
            Id = "evidence-processing",
            Name = "Evidence Processing",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var workflowId =
            await service.StartAsync(definition);

        await service.CompleteAsync(workflowId);

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowStatus.Completed,
            repository.Execution!.CurrentStatus);
    }

    [Fact]
    public async Task CompleteAsync_rejects_already_completed_workflow()
    {
        var repository = new RecordingWorkflowRepository();
        var service = new WorkflowService(repository);

        var definition = CreateDefinition();
        var workflowId = await service.StartAsync(definition);

        await service.CompleteAsync(workflowId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteAsync(workflowId));
    }

    [Fact]
    public async Task FailAsync_rejects_already_failed_workflow()
    {
        var repository = new RecordingWorkflowRepository();
        var service = new WorkflowService(repository);

        var definition = CreateDefinition();
        var workflowId = await service.StartAsync(definition);

        await service.FailAsync(
            workflowId,
            "Activity failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FailAsync(
                workflowId,
                "Activity failed again."));
    }

    [Fact]
    public async Task FailAsync_updates_execution_status()
    {
        var repository = new RecordingWorkflowRepository();
        var service = new WorkflowService(repository);

        var definition = new WorkflowDefinition
        {
            Id = "evidence-processing",
            Name = "Evidence Processing",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var workflowId =
            await service.StartAsync(definition);

        await service.FailAsync(
            workflowId,
            "Activity failed.");

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowStatus.Failed,
            repository.Execution!.CurrentStatus);
    }

    private static WorkflowDefinition CreateDefinition()
{
    return new WorkflowDefinition
    {
        Id = "evidence-processing",
        Name = "Evidence Processing",
        Version = "1",
        ActivityIds = Array.Empty<string>()
    };
}

private sealed class RecordingWorkflowRepository : IWorkflowRepository
    {
        public WorkflowExecutionRecord? Execution { get; private set; }

        public Task CreateExecutionAsync(
            WorkflowExecutionRecord execution,
            CancellationToken cancellationToken = default)
        {
            Execution = execution;
            return Task.CompletedTask;
        }

        public Task UpdateExecutionAsync(
            WorkflowExecutionRecord execution,
            CancellationToken cancellationToken = default)
        {
            Execution = execution;
            return Task.CompletedTask;
        }

        public Task<WorkflowExecutionRecord?> GetExecutionAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Execution);
        }

        public Task AddCheckpointAsync(
            WorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowCheckpoint>>(
                Array.Empty<WorkflowCheckpoint>());
        }
    }
}
