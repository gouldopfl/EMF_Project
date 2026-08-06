namespace EMF.Inventory.Models;

public sealed class ColumnInventory
{
    public string Name { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public bool IsNullable { get; init; }

    public bool IsPrimaryKey { get; init; }

    public string? DefaultValue { get; init; }
}
