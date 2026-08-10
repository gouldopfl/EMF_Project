using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceDevelopmentPlanEvidenceGapTests
{
    [Fact]
    public void Association_PreservesPlanAndEvidenceGap()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-001");

        var evidenceGapId =
            new EvidenceGapId("gap-001");

        var association =
            new EvidenceDevelopmentPlanEvidenceGap
            {
                EvidenceDevelopmentPlanId = planId,
                EvidenceGapId = evidenceGapId
            };

        Assert.Equal(
            planId,
            association.EvidenceDevelopmentPlanId);

        Assert.Equal(
            evidenceGapId,
            association.EvidenceGapId);
    }
}
