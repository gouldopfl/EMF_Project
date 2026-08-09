using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowActivityResolverTests
{
    [Fact]
    public void Resolve_returns_activities_in_definition_order()
    {
        var first = new FakeActivity("first");
        var second = new FakeActivity("second");

        var resolver =
            new WorkflowActivityResolver(new IWorkflowActivity[] { first, second });

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = new[] { "first", "second" }
            };

        var activities = resolver.Resolve(definition);

        Assert.Equal(
            new[] { first, second },
            activities);
    }

    [Fact]
    public void Resolve_rejects_unknown_activity_id()
    {
        var resolver =
            new WorkflowActivityResolver(
                new IWorkflowActivity[] { new FakeActivity("first") });

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = new[] { "unknown" }
            };

        Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(definition));
    }

    [Fact]
    public void Resolve_rejects_duplicate_activity_ids()
    {
        var first = new FakeActivity("first");

        var resolver =
            new WorkflowActivityResolver(
                new IWorkflowActivity[] { first });

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = new[] { "first", "first" }
            };

        Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(definition));
    }


    [Fact]
    public void Constructor_rejects_null_activities()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WorkflowActivityResolver(null!));
    }

    [Fact]
    public void Constructor_rejects_duplicate_registered_activity_ids()
    {
        var first = new FakeActivity("duplicate");
        var second = new FakeActivity("duplicate");

        Assert.Throws<ArgumentException>(
            () => new WorkflowActivityResolver(
                new IWorkflowActivity[] { first, second }));
    }

    private sealed class FakeActivity : IWorkflowActivity
    {
        public FakeActivity(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public string Name => Id;

        public Task<WorkflowActivityResult> ExecuteAsync(
            WorkflowExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new WorkflowActivityResult
                {
                    Succeeded = true,
                    Message = "Completed",
                    CompletedUtc = DateTimeOffset.UtcNow
                });
        }
    }
}
