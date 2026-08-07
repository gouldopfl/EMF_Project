using EMF.Core.Contracts;
using EMF.Core.Models.Identities;

namespace EMF.Core.Models;

public sealed class Artifact : IArtifact
{
    public required ArtifactId Id { get; init; }

    public required string Name { get; init; }

    public required string ArtifactType { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
        = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, object> Metadata { get; init; }
        = new Dictionary<string, object>();
}
