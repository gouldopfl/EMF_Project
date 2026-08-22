namespace EMF.Inventory.Models;

public sealed class DatabaseForeignKey
{
    public required string ColumnName { get; init; }

    public required string ReferencedTable { get; init; }

    public required string ReferencedColumn { get; init; }
}
