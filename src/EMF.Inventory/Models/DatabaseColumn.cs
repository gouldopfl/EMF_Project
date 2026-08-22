namespace EMF.Inventory.Models;

public sealed class DatabaseColumn
{
    public required string Name { get; init; }

    public string DataType { get; init; } = string.Empty;

    public bool IsNullable { get; init; }

    public bool IsPrimaryKey { get; init; }

    public string? DefaultValue { get; init; }
}
