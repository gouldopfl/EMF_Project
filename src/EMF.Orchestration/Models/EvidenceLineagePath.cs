using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class EvidenceLineagePath
{
    public required Artifact StartArtifact { get; init; }

    public required Artifact EndArtifact { get; init; }

    public IReadOnlyList<EvidenceLineageNode> Nodes { get; init; }
        = Array.Empty<EvidenceLineageNode>();
}
