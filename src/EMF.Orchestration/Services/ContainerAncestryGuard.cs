using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ContainerAncestryGuard
{
    public const int DefaultMaxContainerDepth = 8;

    private static readonly HashSet<string> ContainerExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".eml",
            ".msg"
        };

    private readonly IEvidenceRepository _repository;
    private readonly int _maxContainerDepth;

    public ContainerAncestryGuard(
        IEvidenceRepository repository,
        int maxContainerDepth = DefaultMaxContainerDepth)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (maxContainerDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxContainerDepth));
        }

        _repository = repository;
        _maxContainerDepth = maxContainerDepth;
    }

    public async Task ValidateAsync(
        Artifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsContainer(artifact))
            return;

        var pending =
            new Queue<(ArtifactId Id, int Depth)>();

        var bestDepth =
            new Dictionary<ArtifactId, int>
            {
                [artifact.Id] = 1
            };

        pending.Enqueue((artifact.Id, 1));

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            var relationships =
                await _repository.GetRelationshipsAsync(
                    current.Id,
                    cancellationToken);

            foreach (var relationship in relationships)
            {
                if (relationship.SourceArtifactId != current.Id ||
                    relationship.RelationshipType !=
                        RelationshipTypes.DerivedFrom)
                {
                    continue;
                }

                var parent =
                    await _repository.GetArtifactAsync(
                        relationship.TargetArtifactId,
                        cancellationToken);

                if (parent is null)
                {
                    throw new InvalidDataException(
                        "Container ancestry references a missing artifact.");
                }

                if (!IsContainer(parent))
                    continue;

                var nextDepth = current.Depth + 1;

                if (nextDepth > _maxContainerDepth)
                {
                    throw new InvalidDataException(
                        "Container nesting exceeds the maximum allowed depth.");
                }

                if (bestDepth.TryGetValue(
                        parent.Id,
                        out var existingDepth) &&
                    existingDepth >= nextDepth)
                {
                    continue;
                }

                bestDepth[parent.Id] = nextDepth;
                pending.Enqueue((parent.Id, nextDepth));
            }
        }
    }

    private static bool IsContainer(Artifact artifact)
    {
        string? extension = null;

        if (artifact.Metadata.TryGetValue(
                ArtifactMetadataKeys.FileExtension,
                out var value))
        {
            extension =
                value switch
                {
                    string text => text,
                    JsonElement json
                        when json.ValueKind ==
                            JsonValueKind.String =>
                        json.GetString(),
                    _ => null
                };
        }

        if (string.IsNullOrWhiteSpace(extension))
            extension = Path.GetExtension(artifact.Name);

        return !string.IsNullOrWhiteSpace(extension) &&
            ContainerExtensions.Contains(extension);
    }
}
