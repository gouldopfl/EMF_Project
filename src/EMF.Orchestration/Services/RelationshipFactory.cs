using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class RelationshipFactory : IRelationshipFactory
{
    public RelationshipCreationResult Create(
        ArtifactId sourceArtifactId,
        ArtifactId targetArtifactId,
        string relationshipType,
        IReadOnlyDictionary<string, object>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(sourceArtifactId);
        ArgumentNullException.ThrowIfNull(targetArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipType);

        var relationship = new Relationship
        {
            SourceArtifactId = sourceArtifactId,
            TargetArtifactId = targetArtifactId,
            RelationshipType = relationshipType,
            Properties = properties ??
                new Dictionary<string, object>()
        };

        return new RelationshipCreationResult
        {
            Relationship = relationship
        };
    }
}
