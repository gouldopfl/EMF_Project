using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationTimelineServiceTests
{
    [Fact]
    public void Compose_orders_va_and_judicial_events()
    {
        var issueId = new ClaimIssueId("issue-001");

        var vaEntries = new[]
        {
            Entry(issueId, "1", "InitialClaim", 1),
            Entry(issueId, "2", "HigherLevelReview", 3),
            Entry(issueId, "3", "BoardAppeal", 5)
        };

        var courtEvents = new[]
        {
            Event(issueId, "Remand", 9),
            Event(issueId, "CourtAppeal", 7)
        };

        var result =
            new ClaimIssueAdjudicationTimelineService()
                .Compose(vaEntries, courtEvents);

        Assert.Equal(5, result.Count);
        Assert.Equal("InitialClaim", result[0].Description);
        Assert.Equal("HigherLevelReview", result[1].Description);
        Assert.Equal("BoardAppeal", result[2].Description);
        Assert.Equal("CourtAppeal", result[3].EventType);
        Assert.Equal("Remand", result[4].EventType);
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenTimelineRetrievalIsNotConfigured()
    {
        var service =
            new ClaimIssueAdjudicationTimelineService();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.GetAsync(
                        new ClaimIssueId("issue-unconfigured")));

        Assert.Equal(
            "Timeline retrieval is not configured.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_merges_va_and_court_history()
    {
        var issueId = new ClaimIssueId("issue-001");

        var lifecycle =
            new ClaimIssueAdjudicationLifecycleService(
                new DecisionRepository(issueId),
                new SubmissionRepository());

        var service =
            new ClaimIssueAdjudicationTimelineService(
                lifecycle,
                new CourtAppealRepository(issueId));

        var result = await service.GetAsync(issueId);

        Assert.Equal(8, result.Count);

        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.SubmissionSubmitted,
            result[0].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.SubmissionReceived,
            result[1].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.VaDecision,
            result[2].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.SubmissionSubmitted,
            result[3].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.SubmissionReceived,
            result[4].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.VaDecision,
            result[5].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.CourtAppeal,
            result[6].EventType);
        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.Remand,
            result[7].EventType);
    }


    private static ClaimIssueAdjudicationLifecycleEntry Entry(
        ClaimIssueId issueId,
        string suffix,
        string type,
        int month) =>
        new()
        {
            ClaimIssueId = issueId,
            Submission = new()
            {
                Id = new($"submission-{suffix}"),
                ClaimId = new("claim-001"),
                SubmissionType = type
            },
            IssueDecision = new()
            {
                Id = new($"issue-decision-{suffix}"),
                VaDecisionId = new($"decision-{suffix}"),
                ClaimIssueId = issueId,
                Outcome = "Denied"
            },
            VaDecision = new()
            {
                Id = new($"decision-{suffix}"),
                DecisionDate =
                    new DateTimeOffset(
                        2026, month, 1, 0, 0, 0,
                        TimeSpan.Zero)
            }
        };

    private static ClaimIssueAdjudicationEvent Event(
        ClaimIssueId issueId,
        string type,
        int month) =>
        new()
        {
            ClaimIssueId = issueId,
            EventType = type,
            OccurredAt =
                new DateTimeOffset(
                    2026, month, 1, 0, 0, 0,
                    TimeSpan.Zero)
        };

    private sealed class CourtAppealRepository :
        IClaimIssueCourtAppealRepository
    {
        private readonly ClaimIssueId _issueId;

        public CourtAppealRepository(ClaimIssueId issueId)
        {
            _issueId = issueId;
        }

        public Task AddAsync(
            ClaimIssueCourtAppeal appeal,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClaimIssueCourtAppeal>>
            GetByClaimIssueAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaimIssueCourtAppeal>>(
            [
                new()
                {
                    ClaimIssueId = _issueId,
                    Court = "CAVC",
                    FiledAt =
                        new DateTimeOffset(
                            2026, 7, 1, 0, 0, 0,
                            TimeSpan.Zero),
                    DocketNumber = "26-1234",
                    Outcome = "Remanded",
                    DecidedAt =
                        new DateTimeOffset(
                            2026, 9, 1, 0, 0, 0,
                            TimeSpan.Zero)
                }
            ]);
    }



    private sealed class DecisionRepository : IVaDecisionRepository
    {
        private readonly ClaimIssueId _issueId;

        public DecisionRepository(ClaimIssueId issueId)
        {
            _issueId = issueId;
        }

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IssueDecision>>(
            [
                new()
                {
                    Id = new("issue-decision-2"),
                    VaDecisionId = new("decision-2"),
                    ClaimIssueId = _issueId,
                    Outcome = "Denied"
                },
                new()
                {
                    Id = new("issue-decision-1"),
                    VaDecisionId = new("decision-1"),
                    ClaimIssueId = _issueId,
                    Outcome = "Denied"
                }
            ]);

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VaDecision?>(
                new()
                {
                    Id = id,
                    DecisionDate =
                        id.Value == "decision-1"
                            ? new DateTimeOffset(
                                2026, 1, 1, 0, 0, 0,
                                TimeSpan.Zero)
                            : new DateTimeOffset(
                                2026, 5, 1, 0, 0, 0,
                                TimeSpan.Zero)
                });

        public Task<IReadOnlyList<SubmissionId>>
            GetSubmissionIdsAsync(
                IssueDecisionId id,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubmissionId>>(
            [
                new(
                    id.Value == "issue-decision-1"
                        ? "submission-1"
                        : "submission-2")
            ]);

        public Task AddDecisionAsync(
            VaDecision decision,
            IReadOnlyCollection<IssueDecision> issueDecisions,
            IReadOnlyCollection<IssueDecisionSubmission>
                submissionAssociations,
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

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                VaDecisionId vaDecisionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

    }

    private sealed class SubmissionRepository : ISubmissionRepository
    {
        public Task<Submission?> GetSubmissionAsync(
            SubmissionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Submission?>(
                new()
                {
                    Id = id,
                    ClaimId = new("claim-001"),
                    SubmissionType =
                        id.Value == "submission-1"
                            ? "InitialClaim"
                            : "BoardAppeal",
                    SubmittedAt =
                        id.Value == "submission-1"
                            ? new DateTimeOffset(
                                2025, 12, 1, 0, 0, 0,
                                TimeSpan.Zero)
                            : new DateTimeOffset(
                                2026, 4, 1, 0, 0, 0,
                                TimeSpan.Zero),
                    ReceivedAt =
                        id.Value == "submission-1"
                            ? new DateTimeOffset(
                                2025, 12, 2, 0, 0, 0,
                                TimeSpan.Zero)
                            : new DateTimeOffset(
                                2026, 4, 2, 0, 0, 0,
                                TimeSpan.Zero)
                });

        public Task AddSubmissionAsync(
            Submission submission,
            IReadOnlyCollection<ClaimIssueId> claimIssueIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Submission>> GetSubmissionsAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClaimIssueId>> GetClaimIssueIdsAsync(
            SubmissionId submissionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }


}
