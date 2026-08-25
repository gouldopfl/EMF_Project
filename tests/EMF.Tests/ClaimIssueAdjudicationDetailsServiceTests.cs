using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenIssueDoesNotExist()
    {
        var service =
            new ClaimIssueAdjudicationDetailsService(
                new MissingClaimIssueRepository(),
                NeverCall<IConditionRepository>(),
                NeverCall<IServiceConnectionRepository>(),
                NeverCall<IClaimIssueEvidenceDetailsService>());

        var result =
            await service.GetAsync(
                new ClaimIssueId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ComposesAdjudicationDetails()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var condition = new ClaimedCondition
        {
            Id = new ClaimedConditionId("condition-001"),
            ClaimIssueId = issueId,
            Name = "Sleep apnea"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var evidence = new ClaimIssueEvidenceDetails
        {
            ClaimIssue = issue,
            Checklist = new ClaimIssueEvidenceChecklist
            {
                ClaimIssueId = issueId,
                RequirementChecklists = []
            },
            DevelopmentPlans = []
        };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    method =>
                        method.Name == "GetClaimedConditionsAsync"
                            ? Task.FromResult<IReadOnlyList<ClaimedCondition>>(
                                [condition])
                            : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    method =>
                        method.Name == "GetServiceConnectionTheoriesAsync"
                            ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>(
                                [theory])
                            : method.Name == "GetServiceConnectionBasesAsync"
                                ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>(
                                    [basis])
                                : throw new NotSupportedException()),
                Proxy<IClaimIssueEvidenceDetailsService>(
                    method =>
                        method.Name == "GetAsync"
                            ? Task.FromResult<ClaimIssueEvidenceDetails?>(
                                evidence)
                            : throw new NotSupportedException()));

        var result = await service.GetAsync(issueId);

        Assert.NotNull(result);
        Assert.Same(issue, result!.ClaimIssue);
        Assert.Same(condition, Assert.Single(result.ClaimedConditions));
        Assert.Same(theory, Assert.Single(result.ServiceConnectionTheories));
        Assert.Same(basis, Assert.Single(result.ServiceConnectionBases));
        Assert.Same(evidence, result.Evidence);
    }

    private sealed class MissingClaimIssueRepository :
        FakeClaimIssueRepository
    {
        public MissingClaimIssueRepository() : base(null) { }
    }

    private class FakeClaimIssueRepository :
        IClaimIssueRepository
    {
        private readonly ClaimIssue? _issue;

        public FakeClaimIssueRepository(ClaimIssue? issue) =>
            _issue = issue;

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_issue);

        public Task<IReadOnlyList<ClaimIssue>> GetClaimIssuesAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static T NeverCall<T>()
        where T : class =>
        Proxy<T>(
            method => throw new InvalidOperationException(
                $"{method.Name} should not have been called."));

    private static T Proxy<T>(
        Func<MethodInfo, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?>? Handler { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!);
    }

}
