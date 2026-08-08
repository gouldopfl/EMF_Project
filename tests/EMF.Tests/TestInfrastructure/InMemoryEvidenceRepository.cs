using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Tests.TestInfrastructure;

public sealed class InMemoryEvidenceRepository : IEvidenceRepository
{
    private readonly Dictionary<string, Artifact> _artifacts = new();

    private readonly List<Relationship> _relationships = new();

private readonly List<Provenance> _provenance = new();

    public Task AddArtifactAsync(
        Artifact artifact,
        CancellationToken cancellationToken = default)
    {
        _artifacts[artifact.Id.Value] = artifact;

        return Task.CompletedTask;
    }

    public Task AddRelationshipAsync(
        Relationship relationship,
        CancellationToken cancellationToken = default)
    {
        _relationships.Add(relationship);

        return Task.CompletedTask;
    }

    public Task<Artifact?> GetArtifactAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        _artifacts.TryGetValue(
            artifactId.Value,
            out var artifact);

        return Task.FromResult(artifact);
    }

    public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var results = _relationships
            .Where(x =>
                x.SourceArtifactId == artifactId ||
                x.TargetArtifactId == artifactId)
            .ToList();

        return Task.FromResult<IReadOnlyList<Relationship>>(results);
    }


public Task AddProvenanceAsync(
    Provenance provenance,
    CancellationToken cancellationToken = default)
{
    _provenance.Add(provenance);

    return Task.CompletedTask;
}

public Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
    ArtifactId artifactId,
    CancellationToken cancellationToken = default)
{
    var results = _provenance
        .Where(x => x.ArtifactId == artifactId)
        .ToList();

    return Task.FromResult<IReadOnlyList<Provenance>>(results);
}
}
