using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Contracts;

public interface IEvidenceLineageService
{
    Task<IReadOnlyList<Artifact>>
        GetGeneratedFromAncestorsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default);
}
