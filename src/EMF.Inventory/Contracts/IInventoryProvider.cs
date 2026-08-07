using EMF.Inventory.Models;

namespace EMF.Inventory.Contracts;

public interface IInventoryProvider
{
    bool CanHandle(string sourcePath);

    Task<DatabaseInventory> CreateInventoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
