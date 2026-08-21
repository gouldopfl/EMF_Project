using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class EvidenceLineageNode
{
    public required Artifact Artifact { get; init; }

    public required int Depth { get; init; }
}
