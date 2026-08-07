
using EMF.Discovery.Models;
using EMF.Inventory.Contracts;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class InventoryRoutingService : IInventoryRoutingService
{
    private readonly IReadOnlyList<IInventoryProvider> _providers;

    public InventoryRoutingService(
        IEnumerable<IInventoryProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToList();
    }

    public IInventoryProvider? SelectProvider(DiscoveredItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _providers.FirstOrDefault(
            provider => provider.CanHandle(item.SourcePath));
    }}
