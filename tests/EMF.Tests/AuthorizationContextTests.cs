using EMF.Security.Authorization;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class AuthorizationContextTests
{
    [Fact]
    public void Context_PreservesSubjectRolesAndPermissions()
    {
        var context = new AuthorizationContext
        {
            SubjectId = "user-001",
            RoleIds =
            [
                new RoleId("reviewer")
            ],
            PermissionIds =
            [
                new PermissionId("evidence.read")
            ]
        };

        Assert.Equal(
            "user-001",
            context.SubjectId);

        Assert.Single(context.RoleIds);
        Assert.Equal(
            "reviewer",
            context.RoleIds[0].Value);

        Assert.Single(context.PermissionIds);
        Assert.Equal(
            "evidence.read",
            context.PermissionIds[0].Value);
    }
}
