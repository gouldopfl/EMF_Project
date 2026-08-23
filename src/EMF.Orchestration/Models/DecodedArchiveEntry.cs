namespace EMF.Orchestration.Models;

public sealed class DecodedArchiveEntry
{
    public required string EntryName { get; init; }

    public required byte[] Content { get; init; }
}
