
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentPlanStatusServiceTests
{
    [Fact]
    public void Assess_RequiresDevelopmentWhenAnyGapIsOpen()
    {
        var result =
            new EvidenceDevelopmentPlanStatusService()
                .Assess(
                    CreateDetails(
                        ["gap-1", "gap-2"],
                        [
                            Gap("gap-1", EvidenceGapStatuses.Resolved),
                            Gap("gap-2", EvidenceGapStatuses.Open)
                        ]));

        Assert.Equal(
            EvidenceDevelopmentPlanStatuses.RequiresDevelopment,
            result.Status);

        Assert.False(result.IsComplete);
        Assert.True(result.RequiresDevelopment);
    }

    [Fact]
    public void Assess_IsCompleteWhenAllGapsAreResolved()
    {
        var result =
            new EvidenceDevelopmentPlanStatusService()
                .Assess(
                    CreateDetails(
                        ["gap-1", "gap-2"],
                        [
                            Gap("gap-1", EvidenceGapStatuses.Resolved),
                            Gap("gap-2", EvidenceGapStatuses.Resolved)
                        ]));

        Assert.Equal(
            EvidenceDevelopmentPlanStatuses.Complete,
            result.Status);

        Assert.True(result.IsComplete);
        Assert.False(result.RequiresDevelopment);
    }

    [Fact]
    public void Assess_IsUnknownWhenLinkedGapDetailIsMissing()
    {
        var result =
            new EvidenceDevelopmentPlanStatusService()
                .Assess(
                    CreateDetails(
                        ["gap-1", "gap-2"],
                        [
                            Gap("gap-1", EvidenceGapStatuses.Resolved)
                        ]));

        Assert.Equal(
            EvidenceDevelopmentPlanStatuses.Unknown,
            result.Status);

        Assert.False(result.IsComplete);
        Assert.False(result.RequiresDevelopment);
    }

    [Fact]
    public void Assess_IsUnknownWhenGapStatusIsUnrecognized()
    {
        var result =
            new EvidenceDevelopmentPlanStatusService()
                .Assess(
                    CreateDetails(
                        ["gap-1"],
                        [
                            Gap("gap-1", "Unexpected")
                        ]));

        Assert.Equal(
            EvidenceDevelopmentPlanStatuses.Unknown,
            result.Status);
    }

    private static EvidenceDevelopmentPlanDetails CreateDetails(
        IReadOnlyList<string> linkedGapIds,
        IReadOnlyList<EvidenceGap> gapDetails)
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-1");

        return new EvidenceDevelopmentPlanDetails
        {
            Plan =
                new EvidenceDevelopmentPlan
                {
                    Id = planId,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Description = "Plan"
                },
            Requirements = [],
            EvidenceGaps =
                linkedGapIds
                    .Select(
                        id =>
                            new EvidenceDevelopmentPlanEvidenceGap
                            {
                                EvidenceDevelopmentPlanId = planId,
                                EvidenceGapId = new EvidenceGapId(id)
                            })
                    .ToArray(),
            GapDetails = gapDetails,
            Artifacts = [],
            Executions = [],
            Results = []
        };
    }

    private static EvidenceGap Gap(
        string id,
        string status) =>
        new()
        {
            Id = new EvidenceGapId(id),
            ClaimIssueId = new ClaimIssueId("issue-1"),
            RequirementId = new RequirementId("requirement-1"),
            Description = "Missing supporting evidence.",
            Status = status
        };
}
