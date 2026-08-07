using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class ArtifactCreationResult
{
    public required Artifact Artifact { get; init; }

    public required Provenance Provenance { get; init; }
}
