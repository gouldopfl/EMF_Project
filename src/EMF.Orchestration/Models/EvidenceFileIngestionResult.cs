using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class EvidenceFileIngestionResult
{
    public required Artifact Artifact { get; init; }

    public required Provenance Provenance { get; init; }

    public bool AlreadyExisted { get; init; }
}
