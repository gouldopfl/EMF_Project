using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class PermissionTests
{
    [Fact]
    public void Permission_PreservesIdentityAndMetadata()
    {
        var permission = new Permission
        {
            Id = new PermissionId("evidence.read"),
            Name = "Read Evidence",
            Description = "Allows reading evidence."
        };

        Assert.Equal(
            "evidence.read",
            permission.Id.Value);

        Assert.Equal(
            "Read Evidence",
            permission.Name);

        Assert.Equal(
            "Allows reading evidence.",
            permission.Description);
    }
}
