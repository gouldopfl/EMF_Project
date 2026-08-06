using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IRelationship
{
    ArtifactId SourceArtifactId { get; }

    ArtifactId TargetArtifactId { get; }

    string RelationshipType { get; }

    DateTimeOffset CreatedUtc { get; }

    IReadOnlyDictionary<string, object> Properties { get; }
}