using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowRecoveryCoordinatorTests
{
    [Fact]
    public async Task Missing_workflow_returns_failed()
    {
        var repository = new FakeWorkflowRepository();
        var policy = new FakeRecoveryPolicy();

        var coordinator =
            new WorkflowRecoveryCoordinator(
                repository,
                policy);

        var definition = new WorkflowDefinition
        {
            Id = "test",
            Name = "Test Workflow",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var result =
            await coordinator.RecoverAsync(
                new WorkflowId("missing"),
                definition);

        Assert.Equal(
            RecoveryDecision.Failed,
            result);
    }

    [Fact]
    public async Task Existing_workflow_delegates_to_policy()
    {
        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = new WorkflowId("workflow-1"),
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.None
            }
        };

        var policy = new FakeRecoveryPolicy
        {
            Decision = RecoveryDecision.Resume
        };

        var coordinator =
            new WorkflowRecoveryCoordinator(
                repository,
                policy);

        var definition = new WorkflowDefinition
        {
            Id = "test",
            Name = "Test Workflow",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var result =
            await coordinator.RecoverAsync(
                repository.Execution.WorkflowId,
                definition);

        Assert.Equal(
            RecoveryDecision.Resume,
            result);

        Assert.True(policy.WasCalled);

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowRecoveryStatus.Recoverable,
            repository.Execution!.RecoveryStatus);
    }

    private sealed class FakeWorkflowRepository : IWorkflowRepository
    {
        public WorkflowExecutionRecord? Execution { get; set; }

        public Task<WorkflowExecutionRecord?> GetExecutionAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Execution);
        }

        public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowCheckpoint>>(
                Array.Empty<WorkflowCheckpoint>());
        }
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


        public Task AddCheckpointAsync(
            WorkflowCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        }

    private sealed class FakeRecoveryPolicy : IWorkflowRecoveryPolicy
    {
        public RecoveryDecision Decision { get; set; }

        public bool WasCalled { get; private set; }

        public Task<RecoveryDecision> EvaluateAsync(
            WorkflowExecutionRecord execution,
            WorkflowDefinition definition,
            IReadOnlyList<WorkflowCheckpoint> checkpoints,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return Task.FromResult(Decision);
        }
    }
}

public sealed class WorkflowRecoveryCoordinatorStatusTests
{
    [Fact]
    public async Task Require_review_decision_persists_needs_review_status()
    {
        var repository = new TestWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = new WorkflowId("workflow-review"),
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Failed,
                RecoveryStatus = WorkflowRecoveryStatus.None
            }
        };

        var policy = new TestRecoveryPolicy
        {
            Decision = RecoveryDecision.RequireReview
        };

        var coordinator =
            new WorkflowRecoveryCoordinator(
                repository,
                policy);

        var definition = new WorkflowDefinition
        {
            Id = "test",
            Name = "Test Workflow",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var result =
            await coordinator.RecoverAsync(
                repository.Execution.WorkflowId,
                definition);

        Assert.Equal(
            RecoveryDecision.RequireReview,
            result);

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowRecoveryStatus.NeedsReview,
            repository.Execution!.RecoveryStatus);
    }

    private sealed class TestWorkflowRepository : IWorkflowRepository
    {
        public WorkflowExecutionRecord? Execution { get; set; }

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

    private sealed class TestRecoveryPolicy : IWorkflowRecoveryPolicy
    {
        public RecoveryDecision Decision { get; set; }

        public Task<RecoveryDecision> EvaluateAsync(
            WorkflowExecutionRecord execution,
            WorkflowDefinition definition,
            IReadOnlyList<WorkflowCheckpoint> checkpoints,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Decision);
        }
    }
}
