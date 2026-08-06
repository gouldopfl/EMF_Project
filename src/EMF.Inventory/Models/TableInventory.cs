namespace EMF.Inventory.Models;

public sealed class TableInventory
{
    public string Name { get; init; } = string.Empty;

    public long RowCount { get; set; }

    public List<ColumnInventory> Columns { get; } = new();

    public List<string> PrimaryKeys { get; } = new();
}
