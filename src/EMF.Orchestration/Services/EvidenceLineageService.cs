using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EvidenceLineageService : IEvidenceLineageService
{
    private readonly IEvidenceRepository _repository;

    public EvidenceLineageService(
        IEvidenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public async Task<IReadOnlyList<EvidenceLineageNode>>
        GetGeneratedFromAncestorsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<ArtifactId>();
        var ancestors = new List<EvidenceLineageNode>();
        var pending = new Queue<(ArtifactId Id, int Depth)>();

        pending.Enqueue((artifactId, 0));
        visited.Add(artifactId);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var currentId = current.Id;

            var relationships =
                await _repository.GetRelationshipsAsync(
                    currentId,
                    cancellationToken);

            var generatedFromRelationships =
                relationships
                    .Where(
                        relationship =>
                            relationship.SourceArtifactId == currentId &&
                            relationship.RelationshipType ==
                                RelationshipTypes.GeneratedFrom);

            foreach (var relationship in generatedFromRelationships)
            {
                var sourceId = relationship.TargetArtifactId;

                if (!visited.Add(sourceId))
                    continue;

                var artifact =
                    await _repository.GetArtifactAsync(
                        sourceId,
                        cancellationToken);

                if (artifact is null)
                    continue;

                ancestors.Add(
                    new EvidenceLineageNode
                    {
                        Artifact = artifact,
                        Relationship = relationship,
                        Depth = current.Depth + 1
                    });

                pending.Enqueue(
                    (sourceId, current.Depth + 1));
            }
        }

        return ancestors;
    }
    public async Task<IReadOnlyList<EvidenceLineageNode>>
        GetGeneratedFromDescendantsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<ArtifactId>();
        var descendants = new List<EvidenceLineageNode>();
        var pending = new Queue<(ArtifactId Id, int Depth)>();

        pending.Enqueue((artifactId, 0));
        visited.Add(artifactId);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var currentId = current.Id;

            var relationships =
                await _repository.GetRelationshipsAsync(
                    currentId,
                    cancellationToken);

            var generatedFromRelationships =
                relationships
                    .Where(
                        relationship =>
                            relationship.TargetArtifactId == currentId &&
                            relationship.RelationshipType ==
                                RelationshipTypes.GeneratedFrom);

            foreach (var relationship in generatedFromRelationships)
            {
                var descendantId =
                    relationship.SourceArtifactId;

                if (!visited.Add(descendantId))
                    continue;

                var artifact =
                    await _repository.GetArtifactAsync(
                        descendantId,
                        cancellationToken);

                if (artifact is null)
                    continue;

                descendants.Add(
                    new EvidenceLineageNode
                    {
                        Artifact = artifact,
                        Relationship = relationship,
                        Depth = current.Depth + 1
                    });

                pending.Enqueue(
                    (descendantId, current.Depth + 1));
            }
        }

        return descendants;
    }

    public async Task<EvidenceLineagePath?>
        GetGeneratedFromPathAsync(
            ArtifactId startArtifactId,
            ArtifactId endArtifactId,
            CancellationToken cancellationToken = default)
    {
        var startArtifact =
            await _repository.GetArtifactAsync(
                startArtifactId,
                cancellationToken);

        if (startArtifact is null)
            return null;

        if (startArtifactId == endArtifactId)
        {
            return new EvidenceLineagePath
            {
                StartArtifact = startArtifact,
                EndArtifact = startArtifact,
                Nodes = Array.Empty<EvidenceLineageNode>()
            };
        }

        var endArtifact =
            await _repository.GetArtifactAsync(
                endArtifactId,
                cancellationToken);

        if (endArtifact is null)
            return null;

        var visited = new HashSet<ArtifactId>();
        var pending =
            new Queue<(ArtifactId Id, List<EvidenceLineageNode> Path)>();

        visited.Add(startArtifactId);
        pending.Enqueue(
            (startArtifactId, new List<EvidenceLineageNode>()));

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            var relationships =
                await _repository.GetRelationshipsAsync(
                    current.Id,
                    cancellationToken);

            var generatedFromRelationships =
                relationships.Where(
                    relationship =>
                        relationship.SourceArtifactId == current.Id &&
                        relationship.RelationshipType ==
                            RelationshipTypes.GeneratedFrom);

            foreach (var relationship in generatedFromRelationships)
            {
                var nextId = relationship.TargetArtifactId;

                if (!visited.Add(nextId))
                    continue;

                var artifact =
                    await _repository.GetArtifactAsync(
                        nextId,
                        cancellationToken);

                if (artifact is null)
                    continue;

                var nextPath =
                    new List<EvidenceLineageNode>(
                        current.Path)
                    {
                        new EvidenceLineageNode
                        {
                            Artifact = artifact,
                            Relationship = relationship,
                            Depth = current.Path.Count + 1
                        }
                    };

                if (nextId == endArtifactId)
                {
                    return new EvidenceLineagePath
                    {
                        StartArtifact = startArtifact,
                        EndArtifact = endArtifact,
                        Nodes = nextPath
                    };
                }

                pending.Enqueue((nextId, nextPath));
            }
        }

        return null;
    }

}
