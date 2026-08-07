namespace EMF.Discovery.Models;

public sealed class DiscoveredItem
{
    public required string Name { get; init; }

    public required string SourcePath { get; init; }

    public required string SourceType { get; init; }

    public long? SizeBytes { get; init; }

    public DateTimeOffset? CreatedUtc { get; init; }

    public DateTimeOffset? ModifiedUtc { get; init; }

    public IReadOnlyDictionary<string, object> Metadata { get; init; }
        = new Dictionary<string, object>();
}
