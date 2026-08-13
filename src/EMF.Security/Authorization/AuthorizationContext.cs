using EMF.Security.Models.Identities;

namespace EMF.Security.Authorization;

public sealed class AuthorizationContext
{
    public required string SubjectId { get; init; }

    public required IReadOnlyList<RoleId> RoleIds { get; init; }

    public required IReadOnlyList<PermissionId> PermissionIds { get; init; }
}
