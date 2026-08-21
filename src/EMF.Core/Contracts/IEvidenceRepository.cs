using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;

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

    Task<Artifact?> FindArtifactAsync(
        string source,
        ContentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> GetArtifactsByMetadataAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);

    Task AddProvenanceAsync(
        Provenance provenance,
        CancellationToken cancellationToken = default);

    Task AddArtifactWithProvenanceAsync(
        Artifact artifact,
        Provenance provenance,
        CancellationToken cancellationToken = default);

    Task AddArtifactWithProvenanceAndRelationshipsAsync(
        Artifact artifact,
        Provenance provenance,
        IReadOnlyCollection<Relationship> relationships,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
