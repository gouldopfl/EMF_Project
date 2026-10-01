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
    public async Task Definition_id_mismatch_delegates_to_policy()
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
            Decision = RecoveryDecision.RequireReview
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
            RecoveryDecision.RequireReview,
            result.Decision);

        Assert.True(policy.WasCalled);
    }

    [Fact]
    public async Task Definition_version_mismatch_delegates_to_policy()
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
            Version = "2",
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
    public async Task Retry_with_multiple_failed_operations_requires_review()
    {
        var workflowId =
            new WorkflowId("workflow-multiple-failed-operations");

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
                    ActivityId = "First",
                    OperationId = new OperationId("operation-first"),
                    OperationType = "external-side-effect",
                    Status = "Failed",
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                    CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                },
                new WorkflowOperationRecord
                {
                    WorkflowId = workflowId,
                    ActivityId = "Second",
                    OperationId = new OperationId("operation-second"),
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
            ActivityIds = new[] { "First", "Second" }
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

        var saved = Assert.Single(repository.RecoveryDecisions);
        Assert.Equal(RecoveryDecision.RequireReview, saved.Decision);
        Assert.Null(saved.RetryActivityId);
        Assert.Null(saved.RetryOperationId);
    }

    [Fact]
    public async Task Retry_with_failed_operation_missing_activity_id_requires_review()
    {
        var workflowId =
            new WorkflowId("workflow-missing-operation-activity");

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
                    ActivityId = "",
                    OperationId = new OperationId("operation-missing-activity"),
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
            ActivityIds = new[] { "Second" }
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

    [Fact]
    public async Task Recovery_decision_is_persisted_when_status_does_not_change()
    {
        var workflowId = new WorkflowId("workflow-existing-recovery-status");

        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.Recoverable,
                Revision = 7
            }
        };

        var coordinator = new WorkflowRecoveryCoordinator(
            repository,
            new FakeRecoveryPolicy
            {
                Decision = RecoveryDecision.Resume
            });

        var result = await coordinator.RecoverAsync(
            workflowId,
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            });

        Assert.Equal(RecoveryDecision.Resume, result.Decision);

        var saved = Assert.Single(repository.RecoveryDecisions);
        Assert.Equal(workflowId, saved.WorkflowId);
        Assert.Equal("test", saved.DefinitionId);
        Assert.Equal("1", saved.DefinitionVersion);
        Assert.Equal(7, saved.EvaluatedRevision);
        Assert.Equal(RecoveryDecision.Resume, saved.Decision);
        Assert.Null(saved.RetryActivityId);
        Assert.Null(saved.RetryOperationId);

        Assert.NotNull(repository.Execution);
        Assert.Equal(8, repository.Execution!.Revision);
        Assert.Equal(
            WorkflowRecoveryStatus.Recoverable,
            repository.Execution.RecoveryStatus);
    }

    [Fact]
    public async Task Retry_decision_persists_failed_operation_identity()
    {
        var workflowId = new WorkflowId("workflow-retry-persistence");
        var operationId = new OperationId("operation-retry-persistence");

        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.None,
                Revision = 3
            },
            Operations = new[]
            {
                new WorkflowOperationRecord
                {
                    WorkflowId = workflowId,
                    ActivityId = "RetryActivity",
                    OperationId = operationId,
                    OperationType = "external-side-effect",
                    Status = "Failed",
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CompletedUtc = DateTimeOffset.UtcNow
                }
            }
        };

        var coordinator = new WorkflowRecoveryCoordinator(
            repository,
            new FakeRecoveryPolicy
            {
                Decision = RecoveryDecision.Retry
            });

        var result = await coordinator.RecoverAsync(
            workflowId,
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = new[] { "RetryActivity" },
                RetryableActivityIds = new[] { "RetryActivity" }
            });

        Assert.Equal(RecoveryDecision.Retry, result.Decision);
        Assert.Equal("RetryActivity", result.RetryActivityId);
        Assert.Equal(operationId, result.RetryOperationId);

        var saved = Assert.Single(repository.RecoveryDecisions);
        Assert.Equal(3, saved.EvaluatedRevision);
        Assert.Equal(RecoveryDecision.Retry, saved.Decision);
        Assert.Equal("RetryActivity", saved.RetryActivityId);
        Assert.Equal(operationId, saved.RetryOperationId);
    }

    [Fact]
    public async Task Require_review_decision_is_persisted()
    {
        var workflowId = new WorkflowId("workflow-review-persistence");

        var repository = new FakeWorkflowRepository
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.None,
                Revision = 4
            }
        };

        var coordinator = new WorkflowRecoveryCoordinator(
            repository,
            new FakeRecoveryPolicy
            {
                Decision = RecoveryDecision.RequireReview
            });

        var result = await coordinator.RecoverAsync(
            workflowId,
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            });

        Assert.Equal(RecoveryDecision.RequireReview, result.Decision);

        var saved = Assert.Single(repository.RecoveryDecisions);
        Assert.Equal(4, saved.EvaluatedRevision);
        Assert.Equal(RecoveryDecision.RequireReview, saved.Decision);
        Assert.Null(saved.RetryActivityId);
        Assert.Null(saved.RetryOperationId);

        Assert.NotNull(repository.Execution);
        Assert.Equal(
            WorkflowRecoveryStatus.NeedsReview,
            repository.Execution!.RecoveryStatus);
    }

    private sealed class FakeWorkflowRepository : IWorkflowRepository
    {
        public WorkflowExecutionRecord? Execution { get; set; }

        public IReadOnlyList<WorkflowOperationRecord> Operations { get; set; } =
            Array.Empty<WorkflowOperationRecord>();

        public List<WorkflowRecoveryDecisionRecord> RecoveryDecisions { get; } =
            new();

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

        public Task ApplyRecoveryDecisionAsync(
            WorkflowExecutionRecord execution,
            WorkflowRecoveryDecisionRecord decision,
            CancellationToken cancellationToken = default)
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = execution.WorkflowId,
                DefinitionId = execution.DefinitionId,
                DefinitionVersion = execution.DefinitionVersion,
                CreatedUtc = execution.CreatedUtc,
                CurrentStatus = execution.CurrentStatus,
                RecoveryStatus = execution.RecoveryStatus,
                Revision = execution.Revision + 1
            };

            RecoveryDecisions.Add(decision);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowRecoveryDecisionRecord>>
            GetRecoveryDecisionsAsync(
                WorkflowId workflowId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowRecoveryDecisionRecord>>(
                RecoveryDecisions
                    .Where(decision => decision.WorkflowId == workflowId)
                    .ToList());
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

        public List<WorkflowRecoveryDecisionRecord> RecoveryDecisions { get; } =
            new();

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

        public Task ApplyRecoveryDecisionAsync(
            WorkflowExecutionRecord execution,
            WorkflowRecoveryDecisionRecord decision,
            CancellationToken cancellationToken = default)
        {
            Execution = new WorkflowExecutionRecord
            {
                WorkflowId = execution.WorkflowId,
                DefinitionId = execution.DefinitionId,
                DefinitionVersion = execution.DefinitionVersion,
                CreatedUtc = execution.CreatedUtc,
                CurrentStatus = execution.CurrentStatus,
                RecoveryStatus = execution.RecoveryStatus,
                Revision = execution.Revision + 1
            };

            RecoveryDecisions.Add(decision);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowRecoveryDecisionRecord>>
            GetRecoveryDecisionsAsync(
                WorkflowId workflowId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkflowRecoveryDecisionRecord>>(
                RecoveryDecisions
                    .Where(decision => decision.WorkflowId == workflowId)
                    .ToList());
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
