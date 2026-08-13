using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class ProtectionClassificationIdTests
{
    [Fact]
    public void Constructor_PreservesValue()
    {
        var id =
            new ProtectionClassificationId(
                "regulated-health");

        Assert.Equal(
            "regulated-health",
            id.Value);

        Assert.Equal(
            "regulated-health",
            id.ToString());
    }

    [Fact]
    public void Constructor_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(
            () => new ProtectionClassificationId(""));
    }
}
