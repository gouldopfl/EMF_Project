using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationAssessmentServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenDetailsAreMissing()
    {
        var details =
            Proxy<IClaimIssueAdjudicationDetailsService>(
                (method, args) =>
                    Task.FromResult<ClaimIssueAdjudicationDetails?>(null));

        var service =
            new ClaimIssueAdjudicationAssessmentService(
                details,
                new ClaimIssueAdjudicationReadinessService(),
                CreateMeritsService());

        var result =
            await service.GetAsync(
                new ClaimIssueId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ComposesReadiness()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var adjudicationDetails =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions = [],
                ServiceConnectionTheories = [],
                ServiceConnectionBases = [],
                ServiceConnectedConditions = [],
                Requirements = [],
                Evidence =
                    new ClaimIssueEvidenceDetails
                    {
                        ClaimIssue = issue,
                        Checklist =
                            new ClaimIssueEvidenceChecklist
                            {
                                ClaimIssueId = issue.Id,
                                RequirementChecklists = []
                            },
                        DevelopmentPlans = []
                    }
            };

        var details =
            Proxy<IClaimIssueAdjudicationDetailsService>(
                (method, args) =>
                    Task.FromResult<ClaimIssueAdjudicationDetails?>(
                        adjudicationDetails));

        var service =
            new ClaimIssueAdjudicationAssessmentService(
                details,
                new ClaimIssueAdjudicationReadinessService(),
                CreateMeritsService());

        var result =
            await service.GetAsync(issue.Id);

        Assert.NotNull(result);
        Assert.Same(adjudicationDetails, result!.Details);
        Assert.True(result.Readiness.IsReadyForAdjudication);


        Assert.NotNull(result.Merits);

        Assert.Equal(
            FindingOutcomes.Unresolved,
            result.Merits!.Outcome);
    }

    private static ClaimIssueMeritsAssessmentService
        CreateMeritsService()
    {
        var serviceConnections =
            Proxy<IServiceConnectionRepository>(
                (method, args) =>
                    method.Name ==
                        "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<
                                EMF.Extensions.VeteransClaims.Models.Service
                                    .ServiceConnectionTheory>>([])
                        : throw new NotSupportedException());

        var findings =
            Proxy<IFindingRepository>(
                (method, args) =>
                    method.Name == "GetFindingsAsync"
                        ? Task.FromResult<IReadOnlyList<Finding>>([])
                        : throw new NotSupportedException());

        return new ClaimIssueMeritsAssessmentService(
            serviceConnections,
            findings);
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
