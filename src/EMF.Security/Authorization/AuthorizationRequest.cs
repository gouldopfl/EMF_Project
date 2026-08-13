using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Security.Authorization;

public sealed class AuthorizationRequest
{
    public required string SubjectId { get; init; }

    public required PermissionId PermissionId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required ProtectionClassificationId
        ProtectionClassificationId
    { get; init; }
}
