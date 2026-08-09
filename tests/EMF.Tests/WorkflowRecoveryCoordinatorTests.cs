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

        var result =
            await coordinator.RecoverAsync(
                new WorkflowId("missing"));

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

        var result =
            await coordinator.RecoverAsync(
                repository.Execution.WorkflowId);

        Assert.Equal(
            RecoveryDecision.Resume,
            result);

        Assert.True(policy.WasCalled);
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
            IReadOnlyList<WorkflowCheckpoint> checkpoints,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return Task.FromResult(Decision);
        }
    }
}
