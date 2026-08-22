namespace EMF.Inventory.Models;

public sealed class DatabaseStructure
{
    public required string DatabaseEngine { get; init; }

    public string DatabaseVersion { get; set; } = string.Empty;

    public List<DatabaseSchema> Schemas { get; } = new();

    public List<DatabaseView> Views { get; } = new();
}
