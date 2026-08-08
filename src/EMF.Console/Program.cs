using EMF.Discovery.Models;
using EMF.Discovery.Services;
using EMF.Inventory.Providers;
using EMF.Integrity;
using EMF.Orchestration.Services;

var sourcePath = args.Length > 0
    ? args[0]
    : "/opt/emf-lab/datasets/LD-VET-001/extracted";

Console.WriteLine("======================================");
Console.WriteLine(" EMF Discovery + Inventory");
Console.WriteLine("======================================");
Console.WriteLine();
Console.WriteLine($"Source   : {sourcePath}");
Console.WriteLine();

var discovery = new FileSystemDiscoveryService();

var routing = new InventoryRoutingService(
    new[] { new SqliteInventoryProvider() });

var orchestration = new InventoryOrchestrationService(
    discovery,
    routing,
    new ArtifactFactory(),
    new GuidArtifactIdGenerator(),
new Sha256ContentFingerprintService());

await foreach (var result in orchestration.ExecuteAsync(
    sourcePath,
    new DiscoveryOptions()))
{
    Console.WriteLine($"Discovered: {result.DiscoveredItem.SourcePath}");
    Console.WriteLine();

    if (!result.Success || result.Inventory is null)
    {
        Console.WriteLine($"Failed    : {result.Message}");
        Console.WriteLine();
        continue;
    }

    var inventory = result.Inventory;

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

Console.WriteLine("======================================");
Console.WriteLine(" Execution Summary");
Console.WriteLine("======================================");
Console.WriteLine($"Discovered : {orchestration.Statistics.ItemsDiscovered}");
Console.WriteLine($"Handled    : {orchestration.Statistics.ItemsHandled}");
Console.WriteLine($"Skipped    : {orchestration.Statistics.ItemsSkipped}");
Console.WriteLine($"Completed  : {orchestration.Statistics.InventoriesCompleted}");
Console.WriteLine($"Failed     : {orchestration.Statistics.ItemsFailed}");
Console.WriteLine($"Elapsed    : {orchestration.Statistics.Elapsed}");
