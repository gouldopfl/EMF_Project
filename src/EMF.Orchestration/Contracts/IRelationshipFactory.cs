using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IRelationshipFactory
{
    RelationshipCreationResult Create(
        ArtifactId sourceArtifactId,
        ArtifactId targetArtifactId,
        string relationshipType,
        IReadOnlyDictionary<string, object>? properties = null);
}
