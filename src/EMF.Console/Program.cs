using EMF.Inventory.Providers;

const string databasePath =
    "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db";

Console.WriteLine("======================================");
Console.WriteLine(" EMF Inventory");
Console.WriteLine("======================================");
Console.WriteLine();

var provider = new SqliteInventoryProvider();

var inventory = await provider.CreateInventoryAsync(databasePath);

Console.WriteLine($"Database : {inventory.DatabasePath}");
Console.WriteLine($"Engine   : {inventory.DatabaseEngine}");
Console.WriteLine($"Version  : {inventory.DatabaseVersion}");
Console.WriteLine();

Console.WriteLine($"Tables discovered: {inventory.Tables.Count}");
Console.WriteLine();

foreach (var table in inventory.Tables.OrderBy(t => t.Name))
{
    Console.WriteLine($"{table.Name,-30} {table.RowCount,12:N0} rows");
}
