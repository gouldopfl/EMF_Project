using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueMeritsAssessmentServiceTests
{
    [Fact]
    public async Task AssessAsync_ComposesFavorableMerits()
    {
        var issueId = new ClaimIssueId("issue-1");
        var requirementId =
            new RequirementId("requirement-1");

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-1"),
                ClaimIssueId = issueId,
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-1"),
                ClaimIssueId = issueId,
                ServiceConnectionTheoryId = theory.Id
            };

        var connections =
            Proxy<IServiceConnectionRepository>(
                (method, args) =>
                    method.Name ==
                        "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionTheory>>(
                                [theory])
                        : method.Name ==
                            "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ServiceConnectionBasis>>(
                                    [basis])
                            : method.Name ==
                                "GetRequirementIdsAsync"
                                ? Task.FromResult<
                                    IReadOnlyList<RequirementId>>(
                                        [requirementId])
                                : throw new NotSupportedException());

        var findings =
            Proxy<IFindingRepository>(
                (method, args) =>
                    method.Name == "GetFindingsAsync"
                        ? Task.FromResult<IReadOnlyList<Finding>>(
                            [
                                new Finding
                                {
                                    Id =
                                        new FindingId("finding-1"),
                                    ClaimIssueId = issueId,
                                    RequirementId = requirementId,
                                    Outcome =
                                        FindingOutcomes.Favorable,
                                    Description =
                                        "Requirement supported."
                                }
                            ])
                        : throw new NotSupportedException());

        var service =
            new ClaimIssueMeritsAssessmentService(
                connections,
                findings);

        var result =
            await service.AssessAsync(issueId);

        Assert.Equal(issueId, result.ClaimIssueId);
        Assert.Equal(
            FindingOutcomes.Favorable,
            result.Outcome);

        var theoryResult =
            Assert.Single(result.TheoryOutcomes);

        Assert.Equal(
            FindingOutcomes.Favorable,
            theoryResult.Outcome);

        var basisResult =
            Assert.Single(theoryResult.BasisOutcomes);

        Assert.Equal(
            FindingOutcomes.Favorable,
            basisResult.Outcome);

        var requirementResult =
            Assert.Single(basisResult.RequirementOutcomes);

        Assert.Equal(
            requirementId,
            requirementResult.RequirementId);

        Assert.Equal(
            FindingOutcomes.Favorable,
            requirementResult.Outcome);
    }

    [Fact]
    public async Task AssessAsync_ReturnsUnresolvedWhenNoTheoriesExist()
    {
        var issueId = new ClaimIssueId("issue-empty");

        var connections =
            Proxy<IServiceConnectionRepository>(
                (method, args) =>
                    method.Name == "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([])
                        : throw new NotSupportedException());

        var findings =
            Proxy<IFindingRepository>(
                (method, args) => throw new NotSupportedException());

        var result =
            await new ClaimIssueMeritsAssessmentService(
                    connections,
                    findings)
                .AssessAsync(issueId);

        Assert.Equal(FindingOutcomes.Unresolved, result.Outcome);
        Assert.Empty(result.TheoryOutcomes);
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
