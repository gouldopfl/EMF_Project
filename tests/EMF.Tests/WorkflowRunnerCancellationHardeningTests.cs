using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed partial class WorkflowRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_ReleasesClaimOnCallerCancellation()
    {
        var workflowService = new FakeWorkflowService();
        var runner = new WorkflowRunner(workflowService);
        var activity = new CancellableWaitingActivity();

        using var cancellation = new CancellationTokenSource();

        var execution =
            runner.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId(
                            "workflow-caller-cancelled")
                },
                [activity],
                cancellationToken: cancellation.Token);

        await activity.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(1));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution);

        Assert.True(activity.CancellationObserved);

        var operation =
            Assert.Single(workflowService.Operations);

        Assert.Equal("Pending", operation.Status);
        Assert.Empty(workflowService.Checkpoints);
        Assert.False(workflowService.FailCalled);
        Assert.False(workflowService.CompleteCalled);
        Assert.Equal(0, workflowService.ActiveClaimCount);
    }
}
