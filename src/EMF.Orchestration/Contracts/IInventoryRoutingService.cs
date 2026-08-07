using EMF.Discovery.Models;
using EMF.Inventory.Contracts;

namespace EMF.Orchestration.Contracts;

public interface IInventoryRoutingService
{
    IInventoryProvider? SelectProvider(DiscoveredItem item);
}
