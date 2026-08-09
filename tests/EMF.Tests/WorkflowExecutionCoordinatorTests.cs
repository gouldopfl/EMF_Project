using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowExecutionCoordinatorTests
{
    [Fact]
    public async Task Resume_decision_delegates_to_runner()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.Resume
        };

        var runner = new FakeWorkflowRunner();

        var coordinator =
            new WorkflowExecutionCoordinator(
                recoveryCoordinator,
                runner);

        var workflowId =
            new WorkflowId("workflow-resume");

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = new[] { "First" }
            };

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId,
                StartedUtc = DateTimeOffset.UtcNow,
                CurrentStep = "Start"
            };

        var activities =
            Array.Empty<IWorkflowActivity>();

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition,
            context,
            activities);

        Assert.True(runner.WasCalled);
        Assert.Equal(
            workflowId,
            runner.WorkflowId);
    }

    [Fact]
    public async Task Require_review_decision_does_not_run_workflow()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.RequireReview
        };

        var runner = new FakeWorkflowRunner();

        var coordinator =
            new WorkflowExecutionCoordinator(
                recoveryCoordinator,
                runner);

        var workflowId =
            new WorkflowId("workflow-review");

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId,
                StartedUtc = DateTimeOffset.UtcNow,
                CurrentStep = "Start"
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition,
            context,
            Array.Empty<IWorkflowActivity>());

        Assert.False(runner.WasCalled);
    }


    [Fact]
    public async Task Retry_decision_delegates_to_runner()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.Retry
        };

        var runner = new FakeWorkflowRunner();

        var coordinator =
            new WorkflowExecutionCoordinator(
                recoveryCoordinator,
                runner);

        var workflowId =
            new WorkflowId("workflow-retry");

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId,
                StartedUtc = DateTimeOffset.UtcNow,
                CurrentStep = "Start"
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition,
            context,
            Array.Empty<IWorkflowActivity>());

        Assert.True(runner.WasCalled);
    }

    [Theory]
    [InlineData(RecoveryDecision.Failed)]
    [InlineData(RecoveryDecision.Abandoned)]
    public async Task Terminal_decision_does_not_run_workflow(
        RecoveryDecision decision)
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = decision
        };

        var runner = new FakeWorkflowRunner();

        var coordinator =
            new WorkflowExecutionCoordinator(
                recoveryCoordinator,
                runner);

        var workflowId =
            new WorkflowId("workflow-terminal");

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId,
                StartedUtc = DateTimeOffset.UtcNow,
                CurrentStep = "Start"
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition,
            context,
            Array.Empty<IWorkflowActivity>());

        Assert.False(runner.WasCalled);
    }


    [Fact]
    public async Task Mismatched_workflow_ids_are_rejected()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.Resume
        };

        var runner = new FakeWorkflowRunner();

        var coordinator =
            new WorkflowExecutionCoordinator(
                recoveryCoordinator,
                runner);

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-context"),
                StartedUtc = DateTimeOffset.UtcNow,
                CurrentStep = "Start"
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteRecoveryAsync(
                new WorkflowId("workflow-request"),
                definition,
                context,
                Array.Empty<IWorkflowActivity>()));

        Assert.False(runner.WasCalled);
    }

    private sealed class FakeRecoveryCoordinator :
        IWorkflowRecoveryCoordinator
    {
        public RecoveryDecision Decision { get; set; }

        public Task<RecoveryDecision> RecoverAsync(
            WorkflowId workflowId,
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Decision);
        }
    }

    private sealed class FakeWorkflowRunner :
        IWorkflowRunner
    {
        public bool WasCalled { get; private set; }

        public WorkflowId? WorkflowId { get; private set; }

        public Task ExecuteAsync(
            WorkflowExecutionContext context,
            IEnumerable<IWorkflowActivity> activities,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            WorkflowId = context.WorkflowId;

            return Task.CompletedTask;
        }
    }
}
