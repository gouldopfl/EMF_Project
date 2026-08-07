using EMF.Core.Contracts;
using EMF.Core.Models.Identities;

namespace EMF.Core.Models;

public sealed class Provenance : IProvenance
{
    public required ArtifactId ArtifactId { get; init; }

    public required string Source { get; init; }

    public DateTimeOffset RecordedUtc { get; init; }
        = DateTimeOffset.UtcNow;

    public required string RecordedBy { get; init; }

    public IReadOnlyDictionary<string, object> Properties { get; init; }
        = new Dictionary<string, object>();
}
