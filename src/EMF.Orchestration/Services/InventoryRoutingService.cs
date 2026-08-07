using EMF.Discovery.Models;
using EMF.Inventory.Contracts;
using EMF.Inventory.Providers;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class InventoryRoutingService : IInventoryRoutingService
{
    private readonly IInventoryProvider _sqliteProvider;

    public InventoryRoutingService()
        : this(new SqliteInventoryProvider())
    {
    }

    public InventoryRoutingService(IInventoryProvider sqliteProvider)
    {
        ArgumentNullException.ThrowIfNull(sqliteProvider);
        _sqliteProvider = sqliteProvider;
    }

    public IInventoryProvider? SelectProvider(DiscoveredItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var extension = Path.GetExtension(item.SourcePath);

        return extension.Equals(".db", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase)
                ? _sqliteProvider
                : null;
    }
}
