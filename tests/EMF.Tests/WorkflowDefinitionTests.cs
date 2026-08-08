using EMF.Core.Models.Workflow;

namespace EMF.Tests;

public sealed class WorkflowDefinitionTests
{
    [Fact]
    public void Definition_preserves_activity_order()
    {
        var definition = new WorkflowDefinition
        {
            Id = "evidence-processing",
            Name = "Evidence Processing",
            Version = "1",
            ActivityIds = new[]
            {
                "discover",
                "inventory",
                "integrity"
            }
        };

        Assert.Equal(
            new[]
            {
                "discover",
                "inventory",
                "integrity"
            },
            definition.ActivityIds);
    }
}
