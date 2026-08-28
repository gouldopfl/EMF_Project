using System.Reflection;
using EMF.Common;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentCoordinatorTests
{
    [Fact]
    public async Task ProcessAsync_PersistsFullyMatchedDocument()
    {
        var claimId = new ClaimId("claim-1");
        var issueId = new ClaimIssueId("issue-1");

        var repository =
            new RecordingVaDecisionRepository();

        var coordinator =
            CreateCoordinator(
                claimId,
                issueId,
                repository);

        var result =
            await coordinator.ProcessAsync(
                claimId,
                CreateInterpretation("Sleep apnea"));

        Assert.NotNull(result.Decision);
        Assert.Equal(
            new VaDecisionId("va-decision-1"),
            result.Decision.Id);

        Assert.True(result.Persisted);
        Assert.False(result.HasUnresolvedIssues);

        Assert.NotNull(repository.Decision);
        Assert.Single(repository.IssueDecisions);
        Assert.Single(repository.Artifacts);
    }

    [Fact]
    public async Task ProcessAsync_RejectsUnmatchedIssue()
    {
        var claimId = new ClaimId("claim-1");
        var issueId = new ClaimIssueId("issue-1");
        var repository = new RecordingVaDecisionRepository();

        var coordinator =
            CreateCoordinator(
                claimId,
                issueId,
                repository);

        var result =
            await coordinator.ProcessAsync(
                claimId,
                CreateInterpretation("GERD"));

        Assert.Null(result.Decision);
        Assert.False(result.Persisted);
        Assert.True(result.HasUnresolvedIssues);

        var match = Assert.Single(result.Matches);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Unmatched,
            match.Status);
        Assert.Null(match.ClaimIssueId);
        Assert.Empty(match.CandidateClaimIssueIds);

        Assert.Null(repository.Decision);
        Assert.Empty(repository.IssueDecisions);
        Assert.Empty(repository.Artifacts);
    }

    [Fact]
    public async Task ProcessAsync_RejectsAmbiguousIssue()
    {
        var claimId = new ClaimId("claim-1");
        var issueId = new ClaimIssueId("issue-1");
        var secondIssueId = new ClaimIssueId("issue-2");
        var repository = new RecordingVaDecisionRepository();

        var coordinator =
            CreateCoordinator(
                claimId,
                issueId,
                repository,
                secondIssueId);

        var result =
            await coordinator.ProcessAsync(
                claimId,
                CreateInterpretation("Sleep apnea"));

        Assert.Null(result.Decision);
        Assert.False(result.Persisted);
        Assert.True(result.HasUnresolvedIssues);

        var match = Assert.Single(result.Matches);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Ambiguous,
            match.Status);
        Assert.Null(match.ClaimIssueId);
        Assert.Equal(
            new[] { issueId, secondIssueId },
            match.CandidateClaimIssueIds);

        Assert.Null(repository.Decision);
        Assert.Empty(repository.IssueDecisions);
        Assert.Empty(repository.Artifacts);
    }

    [Fact]
    public async Task ProcessAsync_RejectsDocumentWithMixedMatchResults()
    {
        var claimId = new ClaimId("claim-1");
        var issueId = new ClaimIssueId("issue-1");
        var repository = new RecordingVaDecisionRepository();

        var coordinator =
            CreateCoordinator(
                claimId,
                issueId,
                repository);

        var result =
            await coordinator.ProcessAsync(
                claimId,
                CreateInterpretation(
                    "Sleep apnea",
                    "GERD"));

        Assert.Null(result.Decision);
        Assert.False(result.Persisted);
        Assert.True(result.HasUnresolvedIssues);

        Assert.Equal(2, result.Matches.Count);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Matched,
            result.Matches[0].Status);
        Assert.Equal(
            issueId,
            result.Matches[0].ClaimIssueId);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Unmatched,
            result.Matches[1].Status);
        Assert.Null(result.Matches[1].ClaimIssueId);

        Assert.Null(repository.Decision);
        Assert.Empty(repository.IssueDecisions);
        Assert.Empty(repository.Artifacts);
    }

    [Fact]
    public async Task ProcessAsync_PersistsMultipleMatchedIssues()
    {
        var claimId = new ClaimId("claim-1");
        var firstIssueId = new ClaimIssueId("issue-1");
        var secondIssueId = new ClaimIssueId("issue-2");
        var repository = new RecordingVaDecisionRepository();

        var coordinator =
            CreateCoordinator(
                claimId,
                firstIssueId,
                repository,
                secondIssueId,
                "GERD");

        var result =
            await coordinator.ProcessAsync(
                claimId,
                CreateInterpretation(
                    "Sleep apnea",
                    "GERD"));

        Assert.True(result.Persisted);
        Assert.False(result.HasUnresolvedIssues);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(2, repository.IssueDecisions.Count);
        Assert.Single(repository.Artifacts);
    }

    private static VaDecisionDocumentInterpretation
        CreateInterpretation(params string[] descriptions) =>
        new()
        {
            ArtifactId = new ArtifactId("artifact-1"),
            DecisionDate =
                new DateTimeOffset(
                    2026, 8, 27,
                    0, 0, 0,
                    TimeSpan.Zero),
            IssueDecisions =
                descriptions
                    .Select(
                        description =>
                            new VaIssueDecisionInterpretation
                            {
                                IssueDescription = description,
                                Outcome = IssueDecisionOutcomes.Denied,
                                Rationale = "Rationale.",
                                FavorableFindings = [],
                                AdverseFindings = [],
                                CitedRegulations = [],
                                ReferencedEvidence = [],
                                SourceExcerpts =
                                [
                                    new DecisionDocumentSourceExcerpt
                                    {
                                        ArtifactId =
                                            new ArtifactId("artifact-1"),
                                        Text = "Decision text."
                                    }
                                ]
                            })
                    .ToArray()
        };

    private sealed class StubIdGenerator : IIdGenerator
    {
        private readonly Queue<string> _ids;

        public StubIdGenerator(params string[] ids)
        {
            _ids = new Queue<string>(
                ids.Length == 0
                    ? ["issue-decision-1", "va-decision-1"]
                    : ids);
        }

        public string Generate() =>
            _ids.Dequeue();
    }

    private static VaDecisionDocumentCoordinator
        CreateCoordinator(
            ClaimId claimId,
            ClaimIssueId issueId,
            IVaDecisionRepository repository,
            ClaimIssueId? secondIssueId = null,
            string? secondConditionName = null)
    {
        var persistence =
            new VaDecisionDocumentPersistenceService(
                repository,
                new VaDecisionDocumentInterpretationValidator(),
                new VaDecisionDocumentIssueDecisionFactory());

        return new VaDecisionDocumentCoordinator(
            new StubClaimIssueRepository(
                claimId,
                issueId,
                secondIssueId),
            ConditionRepository(
                issueId,
                secondIssueId,
                secondConditionName),
            new VaDecisionDocumentIssueMatchingService(
                new VaDecisionDocumentIssueMatcher()),
            persistence,
            secondConditionName is null
                ? new StubIdGenerator()
                : new StubIdGenerator(
                    "issue-decision-1",
                    "issue-decision-2",
                    "va-decision-1"));
    }

    private sealed class StubClaimIssueRepository :
        IClaimIssueRepository
    {
        private readonly ClaimIssue _issue;

        private readonly ClaimIssue? _secondIssue;

        public StubClaimIssueRepository(
            ClaimId claimId,
            ClaimIssueId issueId,
            ClaimIssueId? secondIssueId)
        {
            _issue = new ClaimIssue
            {
                Id = issueId,
                ClaimId = claimId,
                ClaimIssueType = "Disability"
            };

            if (secondIssueId is not null)
            {
                _secondIssue = new ClaimIssue
                {
                    Id = secondIssueId.Value,
                    ClaimId = claimId,
                    ClaimIssueType = "Disability"
                };
            }
        }

        public Task<IReadOnlyList<ClaimIssue>>
            GetClaimIssuesAsync(
                ClaimId claimId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaimIssue>>(
                _secondIssue is null
                    ? [_issue]
                    : [_issue, _secondIssue]);

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaimIssue?>(_issue);

        public Task AddClaimIssueAsync(
            ClaimIssue claimIssue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static IConditionRepository
        ConditionRepository(
            ClaimIssueId issueId,
            ClaimIssueId? secondIssueId = null,
            string? secondConditionName = null) =>
        Proxy<IConditionRepository>(
            (method, args) =>
            {
                if (method.Name ==
                    nameof(
                        IConditionRepository
                            .GetClaimedConditionsAsync))
                {
                    return Task.FromResult<
                        IReadOnlyList<ClaimedCondition>>(
                        [
                            new ClaimedCondition
                            {
                                Id =
                                    new ClaimedConditionId(
                                        "condition-1"),
                                ClaimIssueId =
                                    (ClaimIssueId)args![0]!,
                                Name =
                                    secondIssueId is not null &&
                                    (ClaimIssueId)args![0]! ==
                                        secondIssueId.Value
                                        ? secondConditionName ??
                                            "Sleep apnea"
                                        : "Sleep apnea"
                            }
                        ]);
                }

                throw new NotSupportedException(
                    method.Name);
            });

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy =
            DispatchProxy.Create<T, TestProxy>();

        ((TestProxy)(object)proxy).Handler =
            handler;

        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>?
            Handler { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!, args);
    }

    private sealed class RecordingVaDecisionRepository :
        IVaDecisionRepository
    {
        public VaDecision? Decision { get; private set; }

        public List<IssueDecision>
            IssueDecisions { get; } = [];

        public List<VaDecisionArtifact>
            Artifacts { get; } = [];

        public Task AddDecisionAsync(
            VaDecision decision,
            IReadOnlyCollection<IssueDecision> issueDecisions,
            IReadOnlyCollection<IssueDecisionSubmission>
                submissionAssociations,
            CancellationToken cancellationToken = default)
        {
            Decision = decision;
            IssueDecisions.AddRange(issueDecisions);

            return Task.CompletedTask;
        }

        public Task AddDecisionArtifactAsync(
            VaDecisionArtifact association,
            CancellationToken cancellationToken = default)
        {
            Artifacts.Add(association);

            return Task.CompletedTask;
        }

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubmissionId>>
            GetSubmissionIdsAsync(
                IssueDecisionId issueDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ArtifactId>>
            GetArtifactIdsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
