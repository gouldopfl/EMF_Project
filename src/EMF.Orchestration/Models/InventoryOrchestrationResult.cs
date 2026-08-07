using EMF.Discovery.Models;
using EMF.Inventory.Models;

namespace EMF.Orchestration.Models;

public sealed class InventoryOrchestrationResult
{
    public required DiscoveredItem DiscoveredItem { get; init; }

    public required DatabaseInventory Inventory { get; init; }
}
