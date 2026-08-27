using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class RequirementFindingAssessmentServiceTests
{
    [Fact]
    public async Task AssessAsync_GroupsFindingsByRequirement()
    {
        var issueId = new ClaimIssueId("issue-1");
        var requirement1 = new RequirementId("requirement-1");
        var requirement2 = new RequirementId("requirement-2");

        var findings =
            Proxy<IFindingRepository>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<Finding>>(
                    [
                        new Finding
                        {
                            Id = new FindingId("finding-1"),
                            ClaimIssueId = issueId,
                            RequirementId = requirement1,
                            Outcome = FindingOutcomes.Favorable,
                            Description = "Requirement 1 supported."
                        },
                        new Finding
                        {
                            Id = new FindingId("finding-2"),
                            ClaimIssueId = issueId,
                            RequirementId = requirement2,
                            Outcome = FindingOutcomes.Unfavorable,
                            Description = "Requirement 2 not supported."
                        }
                    ]));

        var service =
            new RequirementFindingAssessmentService(findings);

        var result =
            await service.AssessAsync(
                issueId,
                [requirement1, requirement2]);

        Assert.Equal(2, result.Count);

        var first =
            Assert.Single(
                result.Single(
                    x => x.RequirementId == requirement1)
                    .Findings);

        Assert.Equal(
            FindingOutcomes.Favorable,
            first.Outcome);

        var second =
            Assert.Single(
                result.Single(
                    x => x.RequirementId == requirement2)
                    .Findings);

        Assert.Equal(
            FindingOutcomes.Unfavorable,
            second.Outcome);
    }

    [Fact]
    public async Task AssessAsync_ReturnsEmptyAssessmentWhenNoFindingExists()
    {
        var issueId = new ClaimIssueId("issue-empty");
        var requirementId =
            new RequirementId("requirement-empty");

        var findings =
            Proxy<IFindingRepository>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<Finding>>(
                        Array.Empty<Finding>()));

        var service =
            new RequirementFindingAssessmentService(findings);

        var result =
            await service.AssessAsync(
                issueId,
                [requirementId]);

        var assessment = Assert.Single(result);

        Assert.Equal(
            requirementId,
            assessment.RequirementId);

        Assert.False(assessment.HasFindings);
        Assert.Empty(assessment.Findings);
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler
            { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!, args);
    }
}
