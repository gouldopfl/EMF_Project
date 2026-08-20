using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceDevelopmentExecutionTests
{
    [Fact]
    public void Execution_PreservesDomainAndWorkflowIdentity()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-1");

        var evidenceGapId =
            new EvidenceGapId("gap-1");

        var workflowId =
            new WorkflowId("workflow-1");

        var execution =
            new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId = planId,
                EvidenceGapId = evidenceGapId,
                WorkflowId = workflowId
            };

        Assert.Equal(
            planId,
            execution.EvidenceDevelopmentPlanId);

        Assert.Equal(
            evidenceGapId,
            execution.EvidenceGapId);

        Assert.Equal(
            workflowId,
            execution.WorkflowId);
    }
}
