using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Tests;

public sealed class WorkflowActivityTests
{
    [Fact]
    public async Task Activity_executes_with_context()
    {
        var activity = new FakeActivity();

        var context = new WorkflowExecutionContext
        {
            WorkflowId = new WorkflowId("workflow-001"),
        };

        var result = await activity.ExecuteAsync(context);

        Assert.True(activity.Executed);

        Assert.Equal(
            "workflow-001",
            activity.Context!.WorkflowId.Value);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "Fake Activity",
            result.ActivityName);
    }


    private sealed class FakeActivity : IWorkflowActivity
    {
        public string Id => "fake-activity";

        public string Name => "Fake Activity";

        public bool Executed { get; private set; }

        public WorkflowExecutionContext? Context { get; private set; }

        public Task<WorkflowActivityResult> ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            Executed = true;

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
}
