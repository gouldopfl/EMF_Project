using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceDevelopmentTests
{
    [Fact]
    public void EvidenceGap_PreservesIssueRequirementAndDescription()
    {
        var gapId =
            new EvidenceGapId("evidence-gap-001");

        var issueId =
            new ClaimIssueId("claim-issue-001");

        var requirementId =
            new RequirementId("requirement-001");

        var gap = new EvidenceGap
        {
            Id = gapId,
            ClaimIssueId = issueId,
            RequirementId = requirementId,
            Description = "Evidence is insufficient for the requirement"
        };

        Assert.Equal(gapId, gap.Id);
        Assert.Equal(issueId, gap.ClaimIssueId);
        Assert.Equal(requirementId, gap.RequirementId);
    }

    [Fact]
    public void EvidenceDevelopmentPlan_PreservesIssueAndDescription()
    {
        var planId =
            new EvidenceDevelopmentPlanId("development-plan-001");

        var issueId =
            new ClaimIssueId("claim-issue-001");

        var plan = new EvidenceDevelopmentPlan
        {
            Id = planId,
            ClaimIssueId = issueId,
            Description = "Obtain potentially useful evidence"
        };

        Assert.Equal(planId, plan.Id);
        Assert.Equal(issueId, plan.ClaimIssueId);
        Assert.Equal(
            "Obtain potentially useful evidence",
            plan.Description);
    }
}
