using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ArtifactSupersessionService
{
    private readonly IEvidenceRepository _repository;

    public ArtifactSupersessionService(
        IEvidenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<ArtifactSupersessionResult> SupersedeAsync(
        ArtifactId replacementArtifactId,
        ArtifactId supersededArtifactId,
        CancellationToken cancellationToken = default)
    {
        if (replacementArtifactId == supersededArtifactId)
        {
            throw new InvalidOperationException(
                "An artifact cannot supersede itself.");
        }

        await GetArtifactAsync(
            replacementArtifactId,
            "Replacement",
            cancellationToken);

        await GetArtifactAsync(
            supersededArtifactId,
            "Superseded",
            cancellationToken);

        var replacementRelationships =
            await GetRelationshipsAsync(
                replacementArtifactId,
                cancellationToken);

        var supersededRelationships =
            await GetRelationshipsAsync(
                supersededArtifactId,
                cancellationToken);

        var exactExisting =
            replacementRelationships
                .Where(
                    relationship =>
                        IsSupersession(relationship) &&
                        relationship.SourceArtifactId ==
                            replacementArtifactId &&
                        relationship.TargetArtifactId ==
                            supersededArtifactId)
                .ToArray();

        if (exactExisting.Length > 1)
        {
            throw new InvalidOperationException(
                "Duplicate artifact supersession relationships were found.");
        }

        var replacementIsSuperseded =
            replacementRelationships.Any(
                relationship =>
                    IsSupersession(relationship) &&
                    relationship.TargetArtifactId ==
                        replacementArtifactId);

        var replacementSupersedesOther =
            replacementRelationships.Any(
                relationship =>
                    IsSupersession(relationship) &&
                    relationship.SourceArtifactId ==
                        replacementArtifactId &&
                    relationship.TargetArtifactId !=
                        supersededArtifactId);

        if (replacementSupersedesOther)
        {
            throw new InvalidOperationException(
                $"Replacement artifact '{replacementArtifactId.Value}' " +
                "already supersedes a different artifact.");
        }

        var supersededByOther =
            supersededRelationships.Any(
                relationship =>
                    IsSupersession(relationship) &&
                    relationship.TargetArtifactId ==
                        supersededArtifactId &&
                    relationship.SourceArtifactId !=
                        replacementArtifactId);

        if (supersededByOther)
        {
            throw new InvalidOperationException(
                $"Artifact '{supersededArtifactId.Value}' has already been " +
                "superseded by a different replacement.");
        }

        if (exactExisting.Length == 1)
        {
            return new ArtifactSupersessionResult
            {
                Relationship = exactExisting[0],
                AlreadyExisted = true
            };
        }

        if (replacementIsSuperseded)
        {
            throw new InvalidOperationException(
                $"Replacement artifact '{replacementArtifactId.Value}' " +
                "has already been superseded.");
        }

        var relationship =
            new Relationship
            {
                SourceArtifactId = replacementArtifactId,
                TargetArtifactId = supersededArtifactId,
                RelationshipType = RelationshipTypes.Supersedes
            };

        await _repository.AddRelationshipAsync(
            relationship,
            cancellationToken);

        return new ArtifactSupersessionResult
        {
            Relationship = relationship,
            AlreadyExisted = false
        };
    }

    public async Task<ArtifactId> ResolveActiveArtifactIdAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<ArtifactId>();
        var current = artifactId;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(current))
            {
                throw new InvalidOperationException(
                    "Artifact supersession cycle detected.");
            }

            await GetArtifactAsync(
                current,
                "Evidence",
                cancellationToken);

            var relationships =
                await GetRelationshipsAsync(
                    current,
                    cancellationToken);

            var outgoingSupersessions =
                relationships
                    .Where(
                        relationship =>
                            IsSupersession(relationship) &&
                            relationship.SourceArtifactId == current)
                    .ToArray();

            if (outgoingSupersessions.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' has invalid multiple " +
                    "supersession predecessors.");
            }

            var incomingSupersessions =
                relationships
                    .Where(
                        relationship =>
                            IsSupersession(relationship) &&
                            relationship.TargetArtifactId == current)
                    .ToArray();

            if (incomingSupersessions.Length == 0)
                return current;

            if (incomingSupersessions.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' has ambiguous active " +
                    "supersession replacements.");
            }

            current = incomingSupersessions[0].SourceArtifactId;
        }
    }

    private async Task<Artifact> GetArtifactAsync(
        ArtifactId artifactId,
        string role,
        CancellationToken cancellationToken)
    {
        var artifact =
            await _repository.GetArtifactAsync(
                artifactId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"{role} artifact not found: '{artifactId.Value}'.");

        if (artifact.Id != artifactId)
        {
            throw new InvalidOperationException(
                $"{role} artifact identity mismatch.");
        }

        return artifact;
    }

    private async Task<IReadOnlyList<Relationship>>
        GetRelationshipsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken)
    {
        var relationships =
            await _repository.GetRelationshipsAsync(
                artifactId,
                cancellationToken);

        if (relationships.Any(
                relationship =>
                    relationship.SourceArtifactId != artifactId &&
                    relationship.TargetArtifactId != artifactId))
        {
            throw new InvalidOperationException(
                $"Relationship lookup for artifact '{artifactId.Value}' " +
                "returned an unrelated relationship.");
        }

        return relationships;
    }

    private static bool IsSupersession(
        Relationship relationship) =>
        string.Equals(
            relationship.RelationshipType,
            RelationshipTypes.Supersedes,
            StringComparison.Ordinal);
}
