using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IArtifact
{
    ArtifactId Id { get; }

    string Name { get; }

    string ArtifactType { get; }

    DateTimeOffset CreatedUtc { get; }

    IReadOnlyDictionary<string, object> Metadata { get; }
}
