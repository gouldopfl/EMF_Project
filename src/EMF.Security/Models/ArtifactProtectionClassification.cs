using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Security.Models;

public sealed class ArtifactProtectionClassification
{
    public required ArtifactId ArtifactId { get; init; }

    public required ProtectionClassificationId
        ProtectionClassificationId
    { get; init; }
}
