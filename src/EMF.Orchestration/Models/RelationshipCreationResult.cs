using EMF.Core.Models;

namespace EMF.Orchestration.Models;

public sealed class RelationshipCreationResult
{
    public required Relationship Relationship { get; init; }
}
