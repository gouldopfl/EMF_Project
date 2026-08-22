namespace EMF.Discovery.Models;

public sealed class ArtifactInspectionResult
{
    public required string SourcePath { get; init; }

    public string? DetectedContentType { get; init; }

    public string? DetectedFormat { get; init; }

    public long? SizeBytes { get; init; }

    public IReadOnlyDictionary<string, object> Metadata { get; init; } =
        new Dictionary<string, object>();

    public IReadOnlyList<string> Findings { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } =
        Array.Empty<string>();
}
