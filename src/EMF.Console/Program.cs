using EMF.Discovery.Models;
using EMF.Discovery.Services;
using EMF.Orchestration.Services;

const string sourcePath =
    "/opt/emf-lab/datasets/LD-VET-001/extracted";

Console.WriteLine("======================================");
Console.WriteLine(" EMF Discovery + Inventory");
Console.WriteLine("======================================");
Console.WriteLine();

var discovery = new FileSystemDiscoveryService();
var routing = new InventoryRoutingService();

await foreach (var item in discovery.DiscoverItemsAsync(
    sourcePath,
    new DiscoveryOptions()))
{
    var provider = routing.SelectProvider(item);

    if (provider is null)
    {
        continue;
    }

    Console.WriteLine($"Discovered: {item.SourcePath}");
    Console.WriteLine();

    var inventory = await provider.CreateInventoryAsync(item.SourcePath);

    Console.WriteLine($"Database : {inventory.DatabasePath}");
    Console.WriteLine($"Engine   : {inventory.DatabaseEngine}");
    Console.WriteLine($"Version  : {inventory.DatabaseVersion}");
    Console.WriteLine();
    Console.WriteLine($"Tables discovered: {inventory.Tables.Count}");
    Console.WriteLine();

    foreach (var table in inventory.Tables.OrderBy(t => t.Name))
    {
        Console.WriteLine(
            $"{table.Name,-30} {table.RowCount,12:N0} rows");
    }

    Console.WriteLine();
}
