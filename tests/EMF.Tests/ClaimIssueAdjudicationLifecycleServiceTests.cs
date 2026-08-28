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
    public async Task GetAsync_orders_initial_and_supplemental_decisions()
    {
        var issueId = new ClaimIssueId("issue-001");

        var decisions = new DecisionRepository(issueId);
        var submissions = new SubmissionRepository();

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
                    Id = new IssueDecisionId("issue-decision-2"),
                    VaDecisionId = new VaDecisionId("decision-2"),
                    ClaimIssueId = _issueId,
                    Outcome = "Granted"
                },
                new()
                {
                    Id = new IssueDecisionId("issue-decision-1"),
                    VaDecisionId = new VaDecisionId("decision-1"),
                    ClaimIssueId = _issueId,
                    Outcome = "Denied"
                }
            ]);

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VaDecision?>(
                new VaDecision
                {
                    Id = id,
                    DecisionDate =
                        id.Value == "decision-1"
                            ? new DateTimeOffset(
                                2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                            : new DateTimeOffset(
                                2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
                });

        public Task<IReadOnlyList<SubmissionId>> GetSubmissionIdsAsync(
            IssueDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubmissionId>>(
            [
                new SubmissionId(
                    id.Value == "issue-decision-1"
                        ? "submission-1"
                        : "submission-2")
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
        public Task<Submission?> GetSubmissionAsync(
            SubmissionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Submission?>(
                new Submission
                {
                    Id = id,
                    ClaimId = new ClaimId("claim-001"),
                    SubmissionType =
                        id.Value == "submission-1"
                            ? SubmissionTypes.InitialClaim
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
