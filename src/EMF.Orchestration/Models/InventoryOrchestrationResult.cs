using EMF.Core.Contracts;
using EMF.Discovery.Models;
using EMF.Inventory.Models;

namespace EMF.Orchestration.Models;

public sealed class InventoryOrchestrationResult : IOperationResult
{
    public required DiscoveredItem DiscoveredItem { get; init; }

    public bool Success { get; init; }

    public string? Message { get; init; }

    public DatabaseInventory? Inventory { get; init; }
}
