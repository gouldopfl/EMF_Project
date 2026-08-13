using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class PermissionIdTests
{
    [Fact]
    public void Constructor_PreservesValue()
    {
        var id = new PermissionId("evidence.read");

        Assert.Equal(
            "evidence.read",
            id.Value);

        Assert.Equal(
            "evidence.read",
            id.ToString());
    }

    [Fact]
    public void Constructor_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(
            () => new PermissionId(""));
    }
}
