using EMF.Security.Models.Identities;

namespace EMF.Security.Authorization;

public sealed class AuthorizationRequest
{
    public required string SubjectId { get; init; }

    public required PermissionId PermissionId { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public required ProtectionClassificationId
        ProtectionClassificationId
    { get; init; }
}
