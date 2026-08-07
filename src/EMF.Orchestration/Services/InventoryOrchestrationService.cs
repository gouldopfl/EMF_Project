using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryOrchestrationService : IInventoryOrchestrationService
{
    private readonly IStreamingDiscoveryService _discovery;
    private readonly IInventoryRoutingService _routing;

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

        await foreach (var item in _discovery.DiscoverItemsAsync(
            sourcePath,
            options,
            cancellationToken))
        {
            var provider = _routing.SelectProvider(item);

            if (provider is null)
            {
                continue;
            }

            var inventory = await provider.CreateInventoryAsync(
                item.SourcePath,
                cancellationToken);

            yield return new InventoryOrchestrationResult
            {
                DiscoveredItem = item,
                Inventory = inventory
            };
        }
    }
}
