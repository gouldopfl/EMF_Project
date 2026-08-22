namespace EMF.Inventory.Models;

public sealed class DatabaseTable
{
    public required string Name { get; init; }

    public long? RowCount { get; init; }

    public List<DatabaseColumn> Columns { get; } = new();

    public List<DatabaseForeignKey> ForeignKeys { get; } = new();

    public List<DatabaseIndex> Indexes { get; } = new();
}
