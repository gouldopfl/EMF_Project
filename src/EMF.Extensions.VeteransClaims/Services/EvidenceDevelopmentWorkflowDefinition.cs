using EMF.Core.Models.Workflow;

namespace EMF.Extensions.VeteransClaims.Services;

public static class EvidenceDevelopmentWorkflowDefinition
{
    public static WorkflowDefinition Create()
    {
        return new WorkflowDefinition
        {
            Id = "veterans-claims-evidence-development",
            Name = "Veterans Claims Evidence Development",
            Version = "1",
            ActivityIds = new[] { "develop-evidence-gap" }
        };
    }
}
