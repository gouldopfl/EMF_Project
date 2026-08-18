using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowExecutionCoordinatorTests
{
    [Fact]
    public async Task Execute_starts_workflow_and_delegates_to_runner()
    {
        var workflowService = new FakeWorkflowService();
        var recoveryCoordinator = new FakeRecoveryCoordinator();
        var runner = new FakeWorkflowRunner();

        var activityResolver =
            new FakeWorkflowActivityResolver();

        var coordinator =
            new WorkflowExecutionCoordinator(
                workflowService,
                recoveryCoordinator,
                activityResolver,
                runner);

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        await coordinator.ExecuteAsync(
            definition);

        Assert.True(workflowService.StartCalled);
        Assert.True(runner.WasCalled);
        Assert.Equal(
            new WorkflowId("workflow-started"),
            runner.WorkflowId!.Value);
    }

    [Fact]
    public async Task Resume_decision_delegates_to_runner()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.Resume
        };

        var workflowService = new FakeWorkflowService();

        var runner = new FakeWorkflowRunner();

        var activityResolver =
            new FakeWorkflowActivityResolver();

        var coordinator =
            new WorkflowExecutionCoordinator(
                workflowService,
                recoveryCoordinator,
                activityResolver,
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
            };

        var activities =
            Array.Empty<IWorkflowActivity>();

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition);

        Assert.True(activityResolver.WasCalled);
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

        var workflowService = new FakeWorkflowService();

        var runner = new FakeWorkflowRunner();

        var activityResolver =
            new FakeWorkflowActivityResolver();

        var coordinator =
            new WorkflowExecutionCoordinator(
                workflowService,
                recoveryCoordinator,
                activityResolver,
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
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition);

        Assert.False(activityResolver.WasCalled);
        Assert.False(activityResolver.WasCalled);
        Assert.False(runner.WasCalled);
    }


    [Fact]
    public async Task Retry_decision_delegates_to_runner()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.Retry
        };

        var workflowService = new FakeWorkflowService();

        var runner = new FakeWorkflowRunner();

        var activityResolver =
            new FakeWorkflowActivityResolver();

        var coordinator =
            new WorkflowExecutionCoordinator(
                workflowService,
                recoveryCoordinator,
                activityResolver,
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
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition);

        Assert.True(activityResolver.WasCalled);
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

        var workflowService = new FakeWorkflowService();

        var runner = new FakeWorkflowRunner();

        var activityResolver =
            new FakeWorkflowActivityResolver();

        var coordinator =
            new WorkflowExecutionCoordinator(
                workflowService,
                recoveryCoordinator,
                activityResolver,
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
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition);

        Assert.False(runner.WasCalled);
    }


    [Fact]
    public async Task Recovery_creates_context_with_requested_workflow_id()
    {
        var recoveryCoordinator = new FakeRecoveryCoordinator
        {
            Decision = RecoveryDecision.Resume
        };

        var workflowService = new FakeWorkflowService();

        var runner = new FakeWorkflowRunner();

        var activityResolver =
            new FakeWorkflowActivityResolver();

        var coordinator =
            new WorkflowExecutionCoordinator(
                workflowService,
                recoveryCoordinator,
                activityResolver,
                runner);

        var workflowId =
            new WorkflowId("workflow-request");

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        await coordinator.ExecuteRecoveryAsync(
            workflowId,
            definition);

        Assert.True(runner.WasCalled);
        Assert.Equal(
            workflowId,
            runner.WorkflowId!.Value);
    }

private sealed class FakeWorkflowActivityResolver : IWorkflowActivityResolver
{
    public bool WasCalled { get; private set; }

    public IReadOnlyList<IWorkflowActivity> Resolve(
        WorkflowDefinition definition)
    {
        WasCalled = true;
        return Array.Empty<IWorkflowActivity>();
    }
}

private sealed class FakeWorkflowService : IWorkflowService
{
    public bool StartCalled { get; private set; }

    public Task<WorkflowId> StartAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        StartCalled = true;
        return Task.FromResult(new WorkflowId("workflow-started"));
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
        => Task.CompletedTask;

    public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WorkflowCheckpoint>>(
            Array.Empty<WorkflowCheckpoint>());

    public Task CompleteAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task FailAsync(
        WorkflowId workflowId,
        string message,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
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
