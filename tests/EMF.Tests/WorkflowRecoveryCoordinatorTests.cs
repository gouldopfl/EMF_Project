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
            result.Decision);
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
            result.Decision);

        Assert.True(policy.WasCalled);

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowRecoveryStatus.Recoverable,
            repository.Execution!.RecoveryStatus);
    }


    [Fact]
    public async Task Definition_id_mismatch_returns_failed_without_policy_evaluation()
    {
        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = new WorkflowId("workflow-id-mismatch"),
                DefinitionId = "original",
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
            Id = "different",
            Name = "Test Workflow",
            Version = "1",
            ActivityIds = Array.Empty<string>()
        };

        var result =
            await coordinator.RecoverAsync(
                repository.Execution.WorkflowId,
                definition);

        Assert.Equal(
            RecoveryDecision.Failed,
            result.Decision);

        Assert.False(policy.WasCalled);
    }

    [Fact]
    public async Task Definition_version_mismatch_returns_failed_without_policy_evaluation()
    {
        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = new WorkflowId("workflow-version-mismatch"),
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
            Version = "2",
            ActivityIds = Array.Empty<string>()
        };

        var result =
            await coordinator.RecoverAsync(
                repository.Execution.WorkflowId,
                definition);

        Assert.Equal(
            RecoveryDecision.Failed,
            result.Decision);

        Assert.False(policy.WasCalled);
    }

    [Fact]
    public async Task Retry_with_failed_operation_missing_from_definition_requires_review()
    {
        var workflowId =
            new WorkflowId("workflow-missing-retry-activity");

        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.None
            },
            Operations = new[]
            {
                new WorkflowOperationRecord
                {
                    WorkflowId = workflowId,
                    ActivityId = "activity-missing",
                    OperationId = new OperationId("operation-missing"),
                    OperationType = "external-side-effect",
                    Status = "Failed",
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CompletedUtc = DateTimeOffset.UtcNow
                }
            }
        };

        var policy = new FakeRecoveryPolicy
        {
            Decision = RecoveryDecision.Retry
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
                workflowId,
                definition);

        Assert.Equal(
            RecoveryDecision.RequireReview,
            result.Decision);

        Assert.Null(result.RetryActivityId);
        Assert.Null(result.RetryOperationId);
    }

    [Fact]
    public async Task Existing_workflow_passes_persisted_operations_to_policy()
    {
        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = new WorkflowId("workflow-operations"),
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.None
            },
            Operations = new[]
            {
                new WorkflowOperationRecord
                {
                    WorkflowId = new WorkflowId("workflow-operations"),
                    ActivityId = "activity-1",
                    OperationId = new OperationId("operation-1"),
                    OperationType = "test-operation",
                    Status = "Pending",
                    CreatedUtc = DateTimeOffset.UtcNow
                }
            }
        };

        var policy = new FakeRecoveryPolicy
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
            result.Decision);

        Assert.True(policy.WasCalled);
        Assert.Single(policy.Operations);
        Assert.Equal(
            "Pending",
            policy.Operations[0].Status);
    }

    private sealed class FakeWorkflowRepository : IWorkflowRepository
    {
        public WorkflowExecutionRecord? Execution { get; set; }

        public IReadOnlyList<WorkflowOperationRecord> Operations { get; set; } =
            Array.Empty<WorkflowOperationRecord>();

        public Task<WorkflowOperationRecord?> GetOperationAsync(
            WorkflowId workflowId,
            string activityId,
            OperationId operationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowOperationRecord?>(null);

        public Task<IReadOnlyList<WorkflowOperationRecord>> GetOperationsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Operations);

        public Task<bool> TryCreateOperationAsync(
            WorkflowOperationRecord operation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task UpdateOperationAsync(
            WorkflowOperationRecord operation,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
        

        public Task AddStatusTransitionAsync(
            WorkflowStatusTransition transition,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowStatusTransition>> GetStatusTransitionsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowStatusTransition>>(
                Array.Empty<WorkflowStatusTransition>());
        }


        public Task ApplyStatusTransitionAsync(
            WorkflowExecutionRecord execution,
            WorkflowStatusTransition transition,
            CancellationToken cancellationToken = default)
        {
            Execution = execution;
            return Task.CompletedTask;
        }
}

    private sealed class FakeRecoveryPolicy : IWorkflowRecoveryPolicy
    {
        public RecoveryDecision Decision { get; set; }

        public IReadOnlyList<WorkflowOperationRecord> Operations { get; private set; } =
            Array.Empty<WorkflowOperationRecord>();

        public bool WasCalled { get; private set; }

        public Task<RecoveryDecision> EvaluateAsync(
            WorkflowExecutionRecord execution,
            WorkflowDefinition definition,
            IReadOnlyList<WorkflowCheckpoint> checkpoints,
            IReadOnlyList<WorkflowOperationRecord> operations,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            Operations = operations;

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
            result.Decision);

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowRecoveryStatus.NeedsReview,
            repository.Execution!.RecoveryStatus);
    }

    private sealed class TestWorkflowRepository : IWorkflowRepository
    {
        public WorkflowExecutionRecord? Execution { get; set; }

        public IReadOnlyList<WorkflowOperationRecord> Operations { get; set; } =
            Array.Empty<WorkflowOperationRecord>();

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

        public Task<WorkflowOperationRecord?> GetOperationAsync(
            WorkflowId workflowId,
            string activityId,
            OperationId operationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowOperationRecord?>(null);

        public Task<IReadOnlyList<WorkflowOperationRecord>> GetOperationsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowOperationRecord>>(
                Array.Empty<WorkflowOperationRecord>());

        public Task<bool> TryCreateOperationAsync(
            WorkflowOperationRecord operation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task UpdateOperationAsync(
            WorkflowOperationRecord operation,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
    

        public Task AddStatusTransitionAsync(
            WorkflowStatusTransition transition,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowStatusTransition>> GetStatusTransitionsAsync(
            WorkflowId workflowId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowStatusTransition>>(
                Array.Empty<WorkflowStatusTransition>());
        }


        public Task ApplyStatusTransitionAsync(
            WorkflowExecutionRecord execution,
            WorkflowStatusTransition transition,
            CancellationToken cancellationToken = default)
        {
            Execution = execution;
            return Task.CompletedTask;
        }
}

    private sealed class TestRecoveryPolicy : IWorkflowRecoveryPolicy
    {
        public RecoveryDecision Decision { get; set; }

        public Task<RecoveryDecision> EvaluateAsync(
            WorkflowExecutionRecord execution,
            WorkflowDefinition definition,
            IReadOnlyList<WorkflowCheckpoint> checkpoints,
            IReadOnlyList<WorkflowOperationRecord> operations,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Decision);
        }
    }
}
