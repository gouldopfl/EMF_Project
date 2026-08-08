using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_runs_activities_in_order()
    {
        var runner = new WorkflowRunner();

        var context = new WorkflowExecutionContext
        {
            WorkflowId = new WorkflowId("workflow-001"),
            StartedUtc = DateTimeOffset.UtcNow,
            CurrentStep = "Start"
        };

        var activities = new[]
        {
            new FakeActivity("First"),
            new FakeActivity("Second")
        };

        await runner.ExecuteAsync(
            context,
            activities);

        Assert.Equal(
            new[] { "First", "Second" },
            activities.Select(x => x.Name));
    }


    private sealed class FakeActivity : IWorkflowActivity
    {
        public FakeActivity(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
