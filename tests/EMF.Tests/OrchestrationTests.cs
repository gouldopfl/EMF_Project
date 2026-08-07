using EMF.Discovery.Models;
using EMF.Inventory.Providers;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class OrchestrationTests
{
    [Fact]
    public void SelectProvider_ForSqliteDatabase_ReturnsSqliteProvider()
    {
        var item = new DiscoveredItem
        {
            Name = "evidence.db",
            SourcePath = "/data/evidence.db",
            SourceType = "file"
        };

        var service = new InventoryRoutingService();

        var provider = service.SelectProvider(item);

        Assert.IsType<SqliteInventoryProvider>(provider);
    }

    [Fact]
    public void SelectProvider_ForUnsupportedFile_ReturnsNull()
    {
        var item = new DiscoveredItem
        {
            Name = "evidence.pdf",
            SourcePath = "/data/evidence.pdf",
            SourceType = "file"
        };

        var service = new InventoryRoutingService();

        var provider = service.SelectProvider(item);

        Assert.Null(provider);
    }
}
