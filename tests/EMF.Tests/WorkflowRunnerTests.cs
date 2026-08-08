using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_runs_activities_in_order_and_records_checkpoints()
    {
        var workflowService = new FakeWorkflowService();
        var runner = new WorkflowRunner(workflowService);
        var executionOrder = new List<string>();

        var context = new WorkflowExecutionContext
        {
            WorkflowId = new WorkflowId("workflow-001"),
            StartedUtc = DateTimeOffset.UtcNow,
            CurrentStep = "Start"
        };

        var activities = new[]
        {
            new FakeActivity("First", executionOrder),
            new FakeActivity("Second", executionOrder)
        };

        await runner.ExecuteAsync(context, activities);

        Assert.Equal(
            new[] { "First", "Second" },
            executionOrder);

        Assert.Equal(2, workflowService.Checkpoints.Count);
        Assert.Equal("First", workflowService.Checkpoints[0].Step);
        Assert.Equal("Second", workflowService.Checkpoints[1].Step);
        Assert.All(
            workflowService.Checkpoints,
            checkpoint => Assert.Equal(
                WorkflowStatus.Completed,
                checkpoint.Status));
    }

    private sealed class FakeActivity : IWorkflowActivity
    {
        private readonly IList<string> _executionOrder;

        public FakeActivity(
            string name,
            IList<string> executionOrder)
        {
            Name = name;
            _executionOrder = executionOrder;
        }

        public string Name { get; }

        public Task<WorkflowActivityResult> ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _executionOrder.Add(Name);

            return Task.FromResult(
                new WorkflowActivityResult
                {
                    ActivityName = Name,
                    Succeeded = true,
                    Message = "Completed",
                    CompletedUtc = DateTimeOffset.UtcNow
                });
        }
    }

    private sealed class FakeWorkflowService : IWorkflowService
    {
        public List<WorkflowCheckpoint> Checkpoints { get; } = new();

        public Task<WorkflowId> StartAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new WorkflowId("workflow-test"));
        }

        public Task RecordCheckpointAsync(
            WorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            Checkpoints.Add(checkpoint);
            return Task.CompletedTask;
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
