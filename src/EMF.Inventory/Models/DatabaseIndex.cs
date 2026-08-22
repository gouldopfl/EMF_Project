namespace EMF.Inventory.Models;

public sealed class DatabaseIndex
{
    public required string Name { get; init; }

    public bool IsUnique { get; init; }

    public List<string> Columns { get; } = new();
}
