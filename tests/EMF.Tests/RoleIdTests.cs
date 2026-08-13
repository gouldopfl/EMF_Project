using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class RoleIdTests
{
    [Fact]
    public void Constructor_PreservesValue()
    {
        var id = new RoleId("mental-health-reviewer");

        Assert.Equal(
            "mental-health-reviewer",
            id.Value);

        Assert.Equal(
            "mental-health-reviewer",
            id.ToString());
    }

    [Fact]
    public void Constructor_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(
            () => new RoleId(""));
    }
}
