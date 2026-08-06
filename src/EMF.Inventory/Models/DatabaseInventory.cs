namespace EMF.Inventory.Models;

public sealed class DatabaseInventory
{
    public string DatabasePath { get; init; } = string.Empty;

    public string DatabaseEngine { get; init; } = string.Empty;

    public string DatabaseVersion { get; set; } = string.Empty;

    public DateTime InventoryDate { get; init; } = DateTime.UtcNow;

    public List<TableInventory> Tables { get; } = new();
}
