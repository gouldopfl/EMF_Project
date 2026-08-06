using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IProvenance
{
    ArtifactId ArtifactId { get; }

    string Source { get; }

    DateTimeOffset RecordedUtc { get; }

    string RecordedBy { get; }

    IReadOnlyDictionary<string, object> Properties { get; }
}