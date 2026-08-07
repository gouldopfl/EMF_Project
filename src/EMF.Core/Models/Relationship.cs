using EMF.Core.Contracts;
using EMF.Core.Models.Identities;

namespace EMF.Core.Models;

public sealed class Relationship : IRelationship
{
    public required ArtifactId SourceArtifactId { get; init; }

    public required ArtifactId TargetArtifactId { get; init; }

    public required string RelationshipType { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
        = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, object> Properties { get; init; }
        = new Dictionary<string, object>();
}
