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
            await service.StartAsync();

        Assert.False(
            string.IsNullOrWhiteSpace(
                workflowId.Value));
    }

    [Fact]
    public async Task RecordCheckpointAsync_stores_checkpoint()
    {
        var service = new FakeWorkflowService();

        var workflowId =
            await service.StartAsync();

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


    private sealed class FakeWorkflowService : IWorkflowService
    {
        public List<WorkflowCheckpoint> Checkpoints { get; } = new();

        public Task<WorkflowId> StartAsync(
            CancellationToken cancellationToken = default)
        {
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
