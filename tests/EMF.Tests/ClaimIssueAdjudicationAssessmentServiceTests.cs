using EMF.Core.Models.Identities;
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
                CreateMeritsService(),
                new ClaimIssueDecisionRecommendationService(),
                CreateReviewHistoryService(),
                new ClaimIssueAdjudicationAgingStatusService(
                    new ClaimIssueAdjudicationAgingService(),
                    new ClaimIssueAdjudicationAgingPolicyService()),
                ClaimIssueAdjudicationAgingPolicies.Default,
                TimeProvider.System);

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
                Timeline = [],
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
                CreateMeritsService(),
                new ClaimIssueDecisionRecommendationService(),
                CreateReviewHistoryService(),
                new ClaimIssueAdjudicationAgingStatusService(
                    new ClaimIssueAdjudicationAgingService(),
                    new ClaimIssueAdjudicationAgingPolicyService()),
                ClaimIssueAdjudicationAgingPolicies.Default,
                TimeProvider.System);

        var result =
            await service.GetAsync(issue.Id);

        Assert.NotNull(result);
        Assert.Same(adjudicationDetails, result!.Details);
        Assert.True(result.Readiness.IsReadyForAdjudication);
        Assert.False(result.RequiresAttention);


        Assert.NotNull(result.Merits);

        Assert.Equal(
            FindingOutcomes.Unresolved,
            result.Merits!.Outcome);

        Assert.NotNull(result.Recommendation);
        Assert.False(result.Recommendation!.HasRecommendation);
        Assert.Null(result.Recommendation.RecommendedOutcome);
    }

    [Fact]
    public async Task GetAsync_composes_decision_review_history()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-history"),
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var decision =
            new IssueDecision
            {
                Id = new IssueDecisionId("decision-history"),
                VaDecisionId = new VaDecisionId("va-decision-history"),
                ClaimIssueId = issue.Id,
                Outcome = IssueDecisionOutcomes.Denied
            };

        var repository =
            new EmptyDecisionRepository
            {
                Decisions = [decision]
            };

        var details =
            CreateDetails(issue, []);

        var reviewHistory =
            new ClaimIssueDecisionReviewHistoryService(
                new ClaimIssueDecisionComparisonHistoryService(
                    repository,
                    new ClaimIssueDecisionComparisonService()),
                new ClaimIssueDecisionReviewService(),
                new ClaimIssueDecisionReviewAnalysisService());

        var service =
            new ClaimIssueAdjudicationAssessmentService(
                Proxy<IClaimIssueAdjudicationDetailsService>(
                    (method, args) =>
                        Task.FromResult<
                            ClaimIssueAdjudicationDetails?>(details)),
                new ClaimIssueAdjudicationReadinessService(),
                CreateMeritsService(),
                new ClaimIssueDecisionRecommendationService(),
                reviewHistory,
                new ClaimIssueAdjudicationAgingStatusService(
                    new ClaimIssueAdjudicationAgingService(),
                    new ClaimIssueAdjudicationAgingPolicyService()),
                ClaimIssueAdjudicationAgingPolicies.Default,
                TimeProvider.System);

        var result =
            await service.GetAsync(issue.Id);

        Assert.NotNull(result);
        Assert.Single(result!.DecisionReviewHistory);
        Assert.Equal(
            issue.Id,
            result.DecisionReviewHistory[0].ClaimIssueId);
    }

    [Fact]
    public async Task GetAsync_surfaces_pending_aging()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-aging"),
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        var details =
            CreateDetails(
                issue,
                [
                    new ClaimIssueAdjudicationEvent
                    {
                        ClaimIssueId = issue.Id,
                        EventType =
                            ClaimIssueAdjudicationEventTypes
                                .SubmissionSubmitted,
                        OccurredAt =
                            new DateTimeOffset(
                                2026, 5, 20, 0, 0, 0,
                                TimeSpan.Zero)
                    }
                ]);

        var service =
            new ClaimIssueAdjudicationAssessmentService(
                Proxy<IClaimIssueAdjudicationDetailsService>(
                    (method, args) =>
                        Task.FromResult<
                            ClaimIssueAdjudicationDetails?>(details)),
                new ClaimIssueAdjudicationReadinessService(),
                CreateMeritsService(),
                new ClaimIssueDecisionRecommendationService(),
                CreateReviewHistoryService(),
                new ClaimIssueAdjudicationAgingStatusService(
                    new ClaimIssueAdjudicationAgingService(),
                    new ClaimIssueAdjudicationAgingPolicyService()),
                ClaimIssueAdjudicationAgingPolicies.Default,
                new FixedTimeProvider(
                    new DateTimeOffset(
                        2026, 8, 28, 0, 0, 0,
                        TimeSpan.Zero)));

        var result =
            await service.GetAsync(issue.Id);

        Assert.NotNull(result);
        Assert.NotNull(result!.Aging);
        Assert.Equal(100, result.Aging!.Aging.AgeInDays);
        Assert.True(result.Readiness.IsReadyForAdjudication);
        Assert.True(result.RequiresAttention);
        Assert.True(result.ShouldConsiderFollowUp);
    }

    private static ClaimIssueDecisionReviewHistoryService
        CreateReviewHistoryService()
    {
        var repository =
            new EmptyDecisionRepository();

        return new ClaimIssueDecisionReviewHistoryService(
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService()),
            new ClaimIssueDecisionReviewService(),
            new ClaimIssueDecisionReviewAnalysisService());
    }

    private sealed class EmptyDecisionRepository :
        IVaDecisionRepository
    {
        public IReadOnlyList<IssueDecision> Decisions { get; init; } = [];

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(Decisions);

        public Task AddDecisionAsync(
            VaDecision decision,
            IReadOnlyCollection<IssueDecision> issueDecisions,
            IReadOnlyCollection<IssueDecisionSubmission> submissionAssociations,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VaDecision?>(
                new VaDecision
                {
                    Id = vaDecisionId,
                    DecisionDate = DateTimeOffset.UnixEpoch
                });

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubmissionId>>
            GetSubmissionIdsAsync(
                IssueDecisionId issueDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddDecisionArtifactAsync(
            VaDecisionArtifact association,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ArtifactId>>
            GetArtifactIdsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static ClaimIssueAdjudicationDetails
        CreateDetails(
            ClaimIssue issue,
            IReadOnlyList<ClaimIssueAdjudicationEvent> timeline)
    {
        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [],
            ServiceConnectionTheories = [],
            ServiceConnectionBases = [],
            ServiceConnectedConditions = [],
            Requirements = [],
            Timeline = timeline,
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
