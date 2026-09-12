using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class ArtifactSupersessionResult
{
    public required Relationship Relationship { get; init; }

    public bool AlreadyExisted { get; init; }
}
