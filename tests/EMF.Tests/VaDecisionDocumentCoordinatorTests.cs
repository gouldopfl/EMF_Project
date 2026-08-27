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

        Assert.Null(repository.Decision);
        Assert.Empty(repository.IssueDecisions);
        Assert.Empty(repository.Artifacts);
    }

    private static VaDecisionDocumentInterpretation
        CreateInterpretation(string description) =>
        new()
        {
            ArtifactId = new ArtifactId("artifact-1"),
            DecisionDate =
                new DateTimeOffset(
                    2026, 8, 27,
                    0, 0, 0,
                    TimeSpan.Zero),
            IssueDecisions =
            [
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
                }
            ]
        };

    private sealed class StubIdGenerator : IIdGenerator
    {
        private readonly Queue<string> _ids =
            new(
                [
                    "issue-decision-1",
                    "va-decision-1"
                ]);

        public string Generate() =>
            _ids.Dequeue();
    }

    private static VaDecisionDocumentCoordinator
        CreateCoordinator(
            ClaimId claimId,
            ClaimIssueId issueId,
            IVaDecisionRepository repository,
            ClaimIssueId? secondIssueId = null)
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
            ConditionRepository(issueId, secondIssueId),
            new VaDecisionDocumentIssueMatchingService(
                new VaDecisionDocumentIssueMatcher()),
            persistence,
            new StubIdGenerator());
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
            ClaimIssueId? secondIssueId = null) =>
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
                                Name = "Sleep apnea"
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
