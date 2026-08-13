using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class RolePermissionTests
{
    [Fact]
    public void RolePermission_PreservesRelationship()
    {
        var relationship = new RolePermission
        {
            RoleId = new RoleId("mental-health-reviewer"),
            PermissionId = new PermissionId("evidence.read")
        };

        Assert.Equal(
            "mental-health-reviewer",
            relationship.RoleId.Value);

        Assert.Equal(
            "evidence.read",
            relationship.PermissionId.Value);
    }
}
