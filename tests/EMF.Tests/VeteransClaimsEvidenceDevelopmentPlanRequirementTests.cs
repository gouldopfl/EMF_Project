using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceDevelopmentPlanRequirementTests
{
    [Fact]
    public void Association_PreservesPlanAndRequirement()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-001");

        var requirementId =
            new RequirementId("requirement-001");

        var association =
            new EvidenceDevelopmentPlanRequirement
            {
                EvidenceDevelopmentPlanId = planId,
                RequirementId = requirementId
            };

        Assert.Equal(
            planId,
            association.EvidenceDevelopmentPlanId);

        Assert.Equal(
            requirementId,
            association.RequirementId);
    }
}
