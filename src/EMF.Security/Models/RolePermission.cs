using EMF.Security.Models.Identities;

namespace EMF.Security.Models;

public sealed class RolePermission
{
    public required RoleId RoleId { get; init; }

    public required PermissionId PermissionId { get; init; }
}
