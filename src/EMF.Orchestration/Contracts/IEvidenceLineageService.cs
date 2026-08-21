using EMF.Orchestration.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Contracts;

public interface IEvidenceLineageService
{
    Task<IReadOnlyList<EvidenceLineageNode>>
        GetGeneratedFromAncestorsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceLineageNode>>
        GetGeneratedFromDescendantsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default);
}
