using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class RoleTests
{
    [Fact]
    public void Role_PreservesIdentityAndMetadata()
    {
        var role = new Role
        {
            Id = new RoleId("mental-health-reviewer"),
            Name = "Mental Health Reviewer",
            Description = "Reviews protected mental health evidence."
        };

        Assert.Equal(
            "mental-health-reviewer",
            role.Id.Value);

        Assert.Equal(
            "Mental Health Reviewer",
            role.Name);

        Assert.Equal(
            "Reviews protected mental health evidence.",
            role.Description);
    }
}
