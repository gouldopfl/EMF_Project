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

        Assert.True(workflowService.CompleteCalled);
        Assert.False(workflowService.FailCalled);
        Assert.Equal(context.WorkflowId, workflowService.CompletedWorkflowId);
    }


    [Fact]
    public async Task ExecuteAsync_skips_completed_activities_when_resuming()
    {
        var workflowService = new FakeWorkflowService();
        var runner = new WorkflowRunner(workflowService);
        var executionOrder = new List<string>();

        var workflowId = new WorkflowId("workflow-002");

        workflowService.Checkpoints.Add(
            new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "First",
                ActivityId = "First",
                Status = WorkflowStatus.Completed,
                RecordedUtc = DateTimeOffset.UtcNow
            });

        var context = new WorkflowExecutionContext
        {
            WorkflowId = workflowId,
        };

        var activities = new[]
        {
            new FakeActivity("First", executionOrder),
            new FakeActivity("Second", executionOrder)
        };

        await runner.ExecuteAsync(context, activities);

        Assert.Equal(
            new[] { "Second" },
            executionOrder);
    }


    [Fact]
    public async Task ExecuteAsync_does_not_repeat_completed_activities_after_runner_restart()
    {
        var workflowService = new FakeWorkflowService();
        var workflowId = new WorkflowId("workflow-restart-001");

        var context = new WorkflowExecutionContext
        {
            WorkflowId = workflowId,
        };

        var firstExecutionOrder = new List<string>();

        var firstRunner = new WorkflowRunner(workflowService);

        var activities = new[]
        {
            new FakeActivity("First", firstExecutionOrder),
            new FakeActivity("Second", firstExecutionOrder)
        };

        await firstRunner.ExecuteAsync(context, activities);

        Assert.Equal(
            new[] { "First", "Second" },
            firstExecutionOrder);

        Assert.Equal(2, workflowService.Checkpoints.Count);

        var secondExecutionOrder = new List<string>();

        var secondRunner = new WorkflowRunner(workflowService);

        var restartedActivities = new[]
        {
            new FakeActivity("First", secondExecutionOrder),
            new FakeActivity("Second", secondExecutionOrder)
        };

        await secondRunner.ExecuteAsync(
            context,
            restartedActivities);

        Assert.Empty(secondExecutionOrder);
    }


    [Fact]
    public async Task ExecuteAsync_stops_after_failed_activity()
    {
        var workflowService = new FakeWorkflowService();
        var runner = new WorkflowRunner(workflowService);
        var executionOrder = new List<string>();

        var context = new WorkflowExecutionContext
        {
            WorkflowId = new WorkflowId("workflow-003"),
        };

        var activities = new[]
        {
            new FakeActivity("First", executionOrder),
            new FakeActivity("Second", executionOrder, succeeded: false),
            new FakeActivity("Third", executionOrder)
        };

        await runner.ExecuteAsync(context, activities);

        Assert.Equal(
            new[] { "First", "Second" },
            executionOrder);

        Assert.Equal(2, workflowService.Checkpoints.Count);
        Assert.Equal(
            WorkflowStatus.Failed,
            workflowService.Checkpoints[1].Status);

        Assert.False(workflowService.CompleteCalled);
        Assert.True(workflowService.FailCalled);
    }


    [Fact]
    public async Task ExecuteAsync_rejects_duplicate_activity_ids()
    {
        var workflowService = new FakeWorkflowService();
        var runner = new WorkflowRunner(workflowService);
        var executionOrder = new List<string>();

        var context = new WorkflowExecutionContext
        {
            WorkflowId = new WorkflowId("workflow-004"),
        };

        var activities = new[]
        {
            new FakeActivity("First", executionOrder, id: "same-id"),
            new FakeActivity("Second", executionOrder, id: "same-id")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ExecuteAsync(context, activities));

        Assert.Empty(executionOrder);
        Assert.Empty(workflowService.Checkpoints);
    }

    private sealed class FakeActivity : IWorkflowActivity
    {
        private readonly IList<string> _executionOrder;
        private readonly bool _succeeded;

        public FakeActivity(
            string name,
            IList<string> executionOrder,
            bool succeeded = true,
            string? id = null)
        {
            Id = id ?? name;
            Name = name;
            _executionOrder = executionOrder;
            _succeeded = succeeded;
        }

        public string Id { get; }

        public string Name { get; }

        public Task<WorkflowActivityResult> ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _executionOrder.Add(Name);

            return Task.FromResult(
                new WorkflowActivityResult
                {
                    Succeeded = _succeeded,
                    Message = "Completed",
                    CompletedUtc = DateTimeOffset.UtcNow
                });
        }
    }

    private sealed class FakeWorkflowService : IWorkflowService
    {
        public List<WorkflowCheckpoint> Checkpoints { get; } = new();

        public bool CompleteCalled { get; private set; }

        public bool FailCalled { get; private set; }

        public WorkflowId? CompletedWorkflowId { get; private set; }

        public Task<WorkflowId> StartAsync(
            WorkflowDefinition definition,
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

        public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowCheckpoint>>(Checkpoints);
        }

        public Task CompleteAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            CompleteCalled = true;
            CompletedWorkflowId = workflowId;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            WorkflowId workflowId,
            string message,
            CancellationToken cancellationToken = default)
        {
            FailCalled = true;
            return Task.CompletedTask;
        }
    }
}
