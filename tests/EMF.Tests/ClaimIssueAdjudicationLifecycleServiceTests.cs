using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationLifecycleServiceTests
{
    [Fact]
    public async Task GetAsync_ThrowsWhenVaDecisionCannotBeRead()
    {
        var service =
            new ClaimIssueAdjudicationLifecycleService(
                new DecisionRepository(
                    new ClaimIssueId("issue-missing-decision")),
                new SubmissionRepository(
                    new ClaimIssueId("issue-missing-decision")));

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.GetAsync(
                        new ClaimIssueId("issue-missing-decision")));

        Assert.Equal(
            "VA decision could not be read.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenSubmissionCannotBeRead()
    {
        var issueId =
            new ClaimIssueId("issue-missing-submission");

        var service =
            new ClaimIssueAdjudicationLifecycleService(
                new DecisionRepository(issueId),
                new SubmissionRepository(issueId));

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Submission could not be read.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_orders_initial_and_supplemental_decisions()
    {
        var issueId = new ClaimIssueId("issue-001");

        var decisions = new DecisionRepository(issueId);
        var submissions = new SubmissionRepository(issueId);

        var service =
            new ClaimIssueAdjudicationLifecycleService(
                decisions,
                submissions);

        var entries = await service.GetAsync(issueId);

        Assert.Equal(2, entries.Count);

        Assert.Equal(
            SubmissionTypes.InitialClaim,
            entries[0].Submission.SubmissionType);

        Assert.Equal(
            "Denied",
            entries[0].IssueDecision.Outcome);

        Assert.Equal(
            SubmissionTypes.SupplementalClaim,
            entries[1].Submission.SubmissionType);

        Assert.Equal(
            "Granted",
            entries[1].IssueDecision.Outcome);
    }


    [Fact]
    public async Task GetAsync_orders_initial_hlr_and_board_decisions()
    {
        var issueId = new ClaimIssueId("issue-review");

        var service =
            new ClaimIssueAdjudicationLifecycleService(
                new DecisionRepository(issueId),
                new SubmissionRepository(issueId));

        var entries = await service.GetAsync(issueId);

        Assert.Equal(3, entries.Count);

        Assert.Equal(
            SubmissionTypes.InitialClaim,
            entries[0].Submission.SubmissionType);

        Assert.Equal(
            SubmissionTypes.HigherLevelReview,
            entries[1].Submission.SubmissionType);

        Assert.Equal(
            SubmissionTypes.BoardAppeal,
            entries[2].Submission.SubmissionType);
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
                CancellationToken cancellationToken = default)
        {
            if (_issueId.Value == "issue-review")
            {
                return Task.FromResult<IReadOnlyList<IssueDecision>>(
                [
                    Decision("3", "Denied"),
                    Decision("1", "Denied"),
                    Decision("2", "Denied")
                ]);
            }

            return Task.FromResult<IReadOnlyList<IssueDecision>>(
            [
                Decision("2", "Granted"),
                Decision("1", "Denied")
            ]);
        }

        private IssueDecision Decision(
            string suffix,
            string outcome) =>
            new()
            {
                Id = new IssueDecisionId(
                    $"issue-decision-{suffix}"),
                VaDecisionId = new VaDecisionId(
                    $"decision-{suffix}"),
                ClaimIssueId = _issueId,
                Outcome = outcome
            };

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VaDecision?>(
                _issueId.Value == "issue-missing-decision"
                    ? null
                    : new VaDecision
                {
                    Id = id,
                    DecisionDate =
                        id.Value switch
                        {
                            "decision-1" =>
                                new DateTimeOffset(
                                    2026, 1, 1, 0, 0, 0,
                                    TimeSpan.Zero),
                            "decision-2" =>
                                new DateTimeOffset(
                                    2026, 6, 1, 0, 0, 0,
                                    TimeSpan.Zero),
                            _ =>
                                new DateTimeOffset(
                                    2026, 9, 1, 0, 0, 0,
                                    TimeSpan.Zero)
                        }
                });

        public Task<IReadOnlyList<SubmissionId>> GetSubmissionIdsAsync(
            IssueDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubmissionId>>(
            [
                new SubmissionId(
                    id.Value switch
                    {
                        "issue-decision-1" => "submission-1",
                        "issue-decision-2" => "submission-2",
                        _ => "submission-3"
                    })
            ]);

        public Task AddDecisionAsync(
            VaDecision d,
            IReadOnlyCollection<IssueDecision> i,
            IReadOnlyCollection<IssueDecisionSubmission> s,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                VaDecisionId id,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddDecisionArtifactAsync(
            VaDecisionArtifact a,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ArtifactId>> GetArtifactIdsAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SubmissionRepository :
        ISubmissionRepository
    {
        private readonly ClaimIssueId _issueId;

        public SubmissionRepository(ClaimIssueId issueId)
        {
            _issueId = issueId;
        }

        public Task<Submission?> GetSubmissionAsync(
            SubmissionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Submission?>(
                _issueId.Value == "issue-missing-submission"
                    ? null
                    : new Submission
                {
                    Id = id,
                    ClaimId = new ClaimId("claim-001"),
                    SubmissionType =
                        id.Value == "submission-1"
                            ? SubmissionTypes.InitialClaim
                            : _issueId.Value == "issue-review"
                                ? id.Value == "submission-2"
                                    ? SubmissionTypes.HigherLevelReview
                                    : SubmissionTypes.BoardAppeal
                                : SubmissionTypes.SupplementalClaim
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
