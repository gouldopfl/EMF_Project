namespace EMF.Inventory.Models;

public sealed class DatabaseSchema
{
    public required string Name { get; init; }

    public List<DatabaseTable> Tables { get; } = new();
}
