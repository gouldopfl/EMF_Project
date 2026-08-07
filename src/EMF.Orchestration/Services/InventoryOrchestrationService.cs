using System.Diagnostics;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryOrchestrationService : IInventoryOrchestrationService
{
    private readonly IStreamingDiscoveryService _discovery;
    private readonly IInventoryRoutingService _routing;

    public InventoryOrchestrationStatistics Statistics { get; private set; }
        = new();

    public InventoryOrchestrationService(
        IStreamingDiscoveryService discovery,
        IInventoryRoutingService routing)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(routing);

        _discovery = discovery;
        _routing = routing;
    }

    public async IAsyncEnumerable<InventoryOrchestrationResult> ExecuteAsync(
        string sourcePath,
        DiscoveryOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        Statistics = new InventoryOrchestrationStatistics();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var item in _discovery.DiscoverItemsAsync(
                sourcePath,
                options,
                cancellationToken))
            {
                Statistics.ItemsDiscovered++;

                var provider = _routing.SelectProvider(item);

                if (provider is null)
                {
                    Statistics.ItemsSkipped++;
                    continue;
                }

                Statistics.ItemsHandled++;

                var inventory = await CreateInventoryAsync(
                    provider,
                    item.SourcePath,
                    cancellationToken);

                Statistics.InventoriesCompleted++;

                yield return new InventoryOrchestrationResult
                {
                    DiscoveredItem = item,
                    Inventory = inventory
                };
            }
        }
        finally
        {
            stopwatch.Stop();
            Statistics.Elapsed = stopwatch.Elapsed;
        }
    }


    private async Task<EMF.Inventory.Models.DatabaseInventory> CreateInventoryAsync(
        EMF.Inventory.Contracts.IInventoryProvider provider,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.CreateInventoryAsync(
                sourcePath,
                cancellationToken);
        }
        catch
        {
            Statistics.ItemsFailed++;
            throw;
        }
    }
}
