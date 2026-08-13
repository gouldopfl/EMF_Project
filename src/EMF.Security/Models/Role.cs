using EMF.Security.Models.Identities;

namespace EMF.Security.Models;

public sealed class Role
{
    public required RoleId Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}
