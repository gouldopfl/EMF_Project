using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class InventoryWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_succeeds_when_all_inventory_results_succeed()
    {
        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                CreateResult(true),
                CreateResult(true)
            }
        };

        var persistence = new FakeEvidencePersistenceService();

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory"),
            };

        var result =
            await activity.ExecuteAsync(context);

        Assert.True(result.Succeeded);
        Assert.Equal(2, persistence.Persisted.Count);
    }

    [Fact]
    public async Task ExecuteAsync_fails_when_any_inventory_result_fails()
    {
        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                CreateResult(true),
                CreateResult(false)
            }
        };

        var persistence = new FakeEvidencePersistenceService();

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory"),
            };

        var result =
            await activity.ExecuteAsync(context);

        Assert.False(result.Succeeded);
        Assert.Single(persistence.Persisted);
    }

    private static InventoryOrchestrationResult CreateResult(
        bool success)
    {
        return new InventoryOrchestrationResult
        {
            DiscoveredItem = null!,
            Artifact = null!,
            Provenance = null!,
            Success = success,
            Inventory = null
        };
    }


    private sealed class FakeEvidencePersistenceService :
        IEvidencePersistenceService
    {
        public List<InventoryOrchestrationResult> Persisted { get; } = [];

        public Task PersistAsync(
            InventoryOrchestrationResult result,
            CancellationToken cancellationToken = default)
        {
            Persisted.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryOrchestrationService :
        IInventoryOrchestrationService
    {
        public List<InventoryOrchestrationResult> Results { get; }
            = new();

        public async IAsyncEnumerable<InventoryOrchestrationResult> ExecuteAsync(
            string sourcePath,
            DiscoveryOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var result in Results)
            {
                yield return result;
                await Task.Yield();
            }
        }
    }
}
