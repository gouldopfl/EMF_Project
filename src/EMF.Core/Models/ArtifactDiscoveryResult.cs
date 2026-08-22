namespace EMF.Core.Models;

public sealed class ArtifactDiscoveryResult
{
    public required string ContentType { get; init; }

    public required string Format { get; init; }

    public double Confidence { get; init; }

    public IReadOnlyDictionary<string, object> Metadata { get; init; } =
        new Dictionary<string, object>();

    public IReadOnlyList<string> Findings { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } =
        Array.Empty<string>();
}
