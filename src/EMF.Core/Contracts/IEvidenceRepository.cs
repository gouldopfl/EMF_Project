using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IEvidenceRepository
{
    Task AddArtifactAsync(
        Artifact artifact,
        CancellationToken cancellationToken = default);

    Task AddRelationshipAsync(
        Relationship relationship,
        CancellationToken cancellationToken = default);

    Task<Artifact?> GetArtifactAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
