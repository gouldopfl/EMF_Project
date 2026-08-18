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

    [Fact]
    public async Task ExecuteAsync_stops_when_activity_claim_is_unavailable()
    {
        var workflowService =
            new FakeWorkflowService
            {
                ClaimAvailable = false
            };

        var executionOrder = new List<string>();
        var runner = new WorkflowRunner(workflowService);

        await runner.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-claimed")
            },
            [new FakeActivity("First", executionOrder)]);

        Assert.Empty(executionOrder);
        Assert.Empty(workflowService.Checkpoints);
        Assert.False(workflowService.CompleteCalled);
        Assert.False(workflowService.FailCalled);
    }

    [Fact]
    public async Task ExecuteAsync_renews_claim_while_activity_is_running()
    {
        var workflowService =
            new FakeWorkflowService();

        var renewalObserved =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        workflowService.OnRenew =
            () => renewalObserved.TrySetResult();

        var runner =
            new WorkflowRunner(
                workflowService,
                new WorkflowActivityClaimHeartbeatOptions(
                    TimeSpan.FromMilliseconds(10)));

        var activity =
            new WaitingActivity(
                renewalObserved.Task);

        await runner.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId =
                    new WorkflowId("workflow-heartbeat")
            },
            [activity]);

        Assert.True(
            workflowService.RenewalCount > 0);
    }


    [Fact]
    public async Task ExecuteAsync_stops_when_claim_renewal_is_lost()
    {
        var workflowService =
            new FakeWorkflowService
            {
                RenewalAvailable = false
            };

        var runner =
            new WorkflowRunner(
                workflowService,
                new WorkflowActivityClaimHeartbeatOptions(
                    TimeSpan.FromMilliseconds(10)));

        var activity =
            new CancellableWaitingActivity();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-heartbeat-lost")
                },
                [activity]));

        Assert.True(activity.CancellationObserved);
        Assert.False(workflowService.CompleteCalled);
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

    private sealed class WaitingActivity :
        IWorkflowActivity
    {
        private readonly Task _release;

        public WaitingActivity(Task release)
        {
            _release = release;
        }

        public string Id => "Waiting";

        public string Name => "Waiting";

        public async Task<WorkflowActivityResult> ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            await _release.WaitAsync(
                cancellationToken);

            return new WorkflowActivityResult
            {
                Succeeded = true,
                Message = "Completed",
                CompletedUtc =
                    DateTimeOffset.UtcNow
            };
        }
    }

    private sealed class CancellableWaitingActivity :
        IWorkflowActivity
    {
        public string Id => "CancellableWaiting";

        public string Name => "CancellableWaiting";

        public bool CancellationObserved { get; private set; }

        public async Task<WorkflowActivityResult> ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            throw new InvalidOperationException(
                "Unreachable.");
        }
    }

    private sealed class FakeWorkflowService : IWorkflowService
    {
        public List<WorkflowCheckpoint> Checkpoints { get; } = new();

        private HashSet<(WorkflowId, string)> Claims { get; } = new();

        public bool ClaimAvailable { get; set; } = true;

        public bool RenewalAvailable { get; set; } = true;

        public bool CompleteCalled { get; private set; }

        public bool FailCalled { get; private set; }

        public int RenewalCount { get; private set; }

        public Action? OnRenew { get; set; }

        public WorkflowId? CompletedWorkflowId { get; private set; }

        public Task<WorkflowId> StartAsync(
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new WorkflowId("workflow-test"));
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
        public Task<bool> TryClaimActivityAsync(
            WorkflowId workflowId,
            string activityId,
            string claimId,
            DateTimeOffset claimedUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                ClaimAvailable && Claims.Add((workflowId, activityId)));
        }

        public Task<bool> TryRenewActivityClaimAsync(
            WorkflowId workflowId,
            string activityId,
            string claimId,
            DateTimeOffset renewedUtc,
            CancellationToken cancellationToken = default)
        {
            RenewalCount++;
            OnRenew?.Invoke();

            return Task.FromResult(
                RenewalAvailable);
        }

        public Task CompleteActivityClaimAsync(
            WorkflowId workflowId,
            string activityId,
            string claimId,
            DateTimeOffset completedUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseActivityClaimAsync(
            WorkflowId workflowId,
            string activityId,
            string claimId,
            CancellationToken cancellationToken = default)
        {
            Claims.Remove((workflowId, activityId));
            return Task.CompletedTask;
        }
    }
}
