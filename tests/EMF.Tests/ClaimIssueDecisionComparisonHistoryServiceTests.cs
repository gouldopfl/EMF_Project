using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueDecisionComparisonHistoryServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsEmptyWhenNoDecisionHistory()
    {
        var issueId = new ClaimIssueId("issue-1");

        var repository =
            Proxy<IVaDecisionRepository>(
                (method, args) =>
                    method.Name == "GetIssueDecisionsAsync" &&
                    args![0] is ClaimIssueId
                        ? Task.FromResult<
                            IReadOnlyList<IssueDecision>>([])
                        : throw new NotSupportedException());

        var service =
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService());

        var result =
            await service.GetAsync(
                CreateRecommendation(issueId));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAsync_ComparesAllHistoricalDecisions()
    {
        var issueId = new ClaimIssueId("issue-1");

        var decisions =
            new[]
            {
                new IssueDecision
                {
                    Id = new IssueDecisionId("decision-1"),
                    VaDecisionId = new VaDecisionId("va-1"),
                    ClaimIssueId = issueId,
                    Outcome = IssueDecisionOutcomes.Denied
                },
                new IssueDecision
                {
                    Id = new IssueDecisionId("decision-2"),
                    VaDecisionId = new VaDecisionId("va-2"),
                    ClaimIssueId = issueId,
                    Outcome = IssueDecisionOutcomes.Granted
                }
            };

        var repository =
            Proxy<IVaDecisionRepository>(
                (method, args) =>
                {
                    if (method.Name == "GetIssueDecisionsAsync" &&
                        args![0] is ClaimIssueId)
                    {
                        return Task.FromResult<
                            IReadOnlyList<IssueDecision>>(decisions);
                    }

                    if (method.Name == "GetDecisionAsync" &&
                        args![0] is VaDecisionId vaDecisionId)
                    {
                        return Task.FromResult<VaDecision?>(
                            new VaDecision
                            {
                                Id = vaDecisionId,
                                DecisionDate =
                                    DateTimeOffset.UnixEpoch
                            });
                    }

                    throw new NotSupportedException();
                });

        var service =
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService());

        var result =
            await service.GetAsync(
                CreateRecommendation(issueId));

        Assert.Equal(2, result.Count);

        Assert.Contains(
            result,
            x =>
                x.IssueDecision.Id ==
                    new IssueDecisionId("decision-1") &&
                x.ComparisonOutcome ==
                    ClaimIssueDecisionComparisonOutcomes.Disagreement);

        Assert.Contains(
            result,
            x =>
                x.IssueDecision.Id ==
                    new IssueDecisionId("decision-2") &&
                x.ComparisonOutcome ==
                    ClaimIssueDecisionComparisonOutcomes.Agreement);
    }

    [Fact]
    public async Task GetAsync_IncludesParentVaDecision()
    {
        var issueId = new ClaimIssueId("issue-1");
        var vaDecisionId = new VaDecisionId("va-1");

        var issueDecision =
            new IssueDecision
            {
                Id = new IssueDecisionId("decision-1"),
                VaDecisionId = vaDecisionId,
                ClaimIssueId = issueId,
                Outcome = IssueDecisionOutcomes.Denied
            };

        var vaDecision =
            new VaDecision
            {
                Id = vaDecisionId,
                DecisionDate =
                    new DateTimeOffset(
                        2026, 8, 11, 0, 0, 0,
                        TimeSpan.Zero)
            };

        var repository =
            Proxy<IVaDecisionRepository>(
                (method, args) =>
                {
                    if (method.Name == "GetIssueDecisionsAsync" &&
                        args![0] is ClaimIssueId)
                    {
                        return Task.FromResult<
                            IReadOnlyList<IssueDecision>>(
                                [issueDecision]);
                    }

                    if (method.Name == "GetDecisionAsync")
                    {
                        return Task.FromResult<VaDecision?>(
                            vaDecision);
                    }

                    throw new NotSupportedException();
                });

        var service =
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService());

        var result =
            await service.GetAsync(
                CreateRecommendation(issueId));

        var comparison = Assert.Single(result);

        Assert.Same(
            vaDecision,
            comparison.VaDecision);
    }

    [Fact]
    public async Task GetAsync_QueriesRecommendationClaimIssue()
    {
        var issueId = new ClaimIssueId("issue-expected");
        ClaimIssueId? queried = null;

        var repository =
            Proxy<IVaDecisionRepository>(
                (method, args) =>
                {
                    if (method.Name == "GetIssueDecisionsAsync" &&
                        args![0] is ClaimIssueId claimIssueId)
                    {
                        queried = claimIssueId;

                        return Task.FromResult<
                            IReadOnlyList<IssueDecision>>([]);
                    }

                    throw new NotSupportedException();
                });

        var service =
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService());

        await service.GetAsync(
            CreateRecommendation(issueId));

        Assert.Equal(issueId, queried);
    }

    private static ClaimIssueDecisionRecommendation
        CreateRecommendation(ClaimIssueId issueId)
    {
        return new ClaimIssueDecisionRecommendation
        {
            ClaimIssueId = issueId,
            IsReadyForAdjudication = true,
            MeritsOutcome = FindingOutcomes.Favorable,
            RecommendedOutcome = IssueDecisionOutcomes.Granted
        };
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
