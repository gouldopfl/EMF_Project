namespace EMF.Core.Models;

public sealed class EvidenceAggregate
{
    public required Artifact Artifact { get; init; }

    public IReadOnlyList<Provenance> Provenance { get; init; }
        = Array.Empty<Provenance>();

    public IReadOnlyList<Relationship> Relationships { get; init; }
        = Array.Empty<Relationship>();
}
