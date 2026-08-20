using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentWorkflowDefinitionTests
{
    [Fact]
    public void Create_ReturnsStableDefinition()
    {
        var definition =
            EvidenceDevelopmentWorkflowDefinition.Create();

        Assert.Equal(
            "veterans-claims-evidence-development",
            definition.Id);

        Assert.Equal(
            "Veterans Claims Evidence Development",
            definition.Name);

        Assert.Equal("1", definition.Version);

        Assert.Equal(
            new[] { "develop-evidence-gap" },
            definition.ActivityIds);
    }
}
