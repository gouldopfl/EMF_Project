using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Security.Storage.Models;

public sealed class ArtifactEnvelopeRewrappingRequest
{
    public required string SubjectId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required ProtectionClassificationId
        ProtectionClassificationId
    { get; init; }
}
