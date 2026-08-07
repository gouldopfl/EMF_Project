using EMF.Inventory.Models;

namespace EMF.Inventory.Contracts;

public interface IInventoryProvider
{
    Task<DatabaseInventory> CreateInventoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
