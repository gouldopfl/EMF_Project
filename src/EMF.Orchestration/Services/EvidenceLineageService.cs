using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;

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

    public async Task<IReadOnlyList<Artifact>>
        GetGeneratedFromAncestorsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<ArtifactId>();
        var ancestors = new List<Artifact>();
        var pending = new Queue<ArtifactId>();

        pending.Enqueue(artifactId);
        visited.Add(artifactId);

        while (pending.Count > 0)
        {
            var currentId = pending.Dequeue();

            var relationships =
                await _repository.GetRelationshipsAsync(
                    currentId,
                    cancellationToken);

            var sourceIds =
                relationships
                    .Where(
                        relationship =>
                            relationship.SourceArtifactId == currentId &&
                            relationship.RelationshipType ==
                                RelationshipTypes.GeneratedFrom)
                    .Select(
                        relationship =>
                            relationship.TargetArtifactId)
                    .Distinct();

            foreach (var sourceId in sourceIds)
            {
                if (!visited.Add(sourceId))
                    continue;

                var artifact =
                    await _repository.GetArtifactAsync(
                        sourceId,
                        cancellationToken);

                if (artifact is null)
                    continue;

                ancestors.Add(artifact);
                pending.Enqueue(sourceId);
            }
        }

        return ancestors;
    }
}
