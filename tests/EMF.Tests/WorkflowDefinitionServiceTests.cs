using EMF.Core.Contracts;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowDefinitionServiceTests
{
    [Fact]
    public async Task Register_delegates_to_repository()
    {
        var repository =
            new FakeWorkflowDefinitionRepository();

        var service =
            new WorkflowDefinitionService(repository);

        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

        await service.RegisterAsync(definition);

        Assert.True(repository.StoreCalled);
        Assert.Same(
            definition,
            repository.StoredDefinition);
    }

    [Fact]
    public async Task Resolve_returns_exact_definition_version()
    {
        var definition =
            new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "2",
                ActivityIds = new[] { "first" }
            };

        var repository =
            new FakeWorkflowDefinitionRepository
            {
                Definition = definition
            };

        var service =
            new WorkflowDefinitionService(repository);

        var result =
            await service.ResolveAsync(
                "test",
                "2");

        Assert.Same(
            definition,
            result);

        Assert.Equal(
            "test",
            repository.RequestedDefinitionId);

        Assert.Equal(
            "2",
            repository.RequestedVersion);
    }

    private sealed class FakeWorkflowDefinitionRepository :
        IWorkflowDefinitionRepository
    {
        public bool StoreCalled { get; private set; }

        public WorkflowDefinition? StoredDefinition { get; private set; }

        public WorkflowDefinition? Definition { get; set; }

        public string? RequestedDefinitionId { get; private set; }

        public string? RequestedVersion { get; private set; }

        public Task StoreDefinitionAsync(
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
        {
            StoreCalled = true;
            StoredDefinition = definition;

            return Task.CompletedTask;
        }

        public Task<WorkflowDefinition?> GetDefinitionAsync(
            string definitionId,
            string version,
            CancellationToken cancellationToken = default)
        {
            RequestedDefinitionId = definitionId;
            RequestedVersion = version;

            return Task.FromResult(Definition);
        }
    }
}
