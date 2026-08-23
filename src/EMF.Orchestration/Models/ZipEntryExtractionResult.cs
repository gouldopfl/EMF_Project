using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class ZipEntryExtractionResult
{
    public required Artifact Artifact { get; init; }

    public required Provenance Provenance { get; init; }

    public required IReadOnlyList<Relationship> Relationships { get; init; }
}
