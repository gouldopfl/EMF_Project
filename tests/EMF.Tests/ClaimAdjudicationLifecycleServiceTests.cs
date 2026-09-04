using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimAdjudicationLifecycleServiceTests
{
    [Fact]
    public async Task GetAsync_RejectsIssueForDifferentClaim()
    {
        var requestedClaimId = new ClaimId("claim-1");
        var wrongClaimId = new ClaimId("claim-other");

        var service =
            new ClaimAdjudicationLifecycleService(
                new FakeClaimIssueRepository(
                    CreateIssue("issue-wrong", wrongClaimId)),
                CreateIssueLifecycleService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(requestedClaimId));
    }

    [Fact]
    public async Task GetAsync_CombinesClaimIssueLifecycles()
    {
        var claimId = new ClaimId("claim-1");

        var issue1 = CreateIssue("issue-1", claimId);
        var issue2 = CreateIssue("issue-2", claimId);

        var service =
            new ClaimAdjudicationLifecycleService(
                new FakeClaimIssueRepository(issue1, issue2),
                CreateIssueLifecycleService());

        var result = await service.GetAsync(claimId);

        Assert.Equal(2, result.Count);
        Assert.Equal(
            "issue-2",
            result[0].ClaimIssueId.Value);
        Assert.Equal(
            "issue-1",
            result[1].ClaimIssueId.Value);
    }

    private static ClaimIssueAdjudicationLifecycleService
        CreateIssueLifecycleService() =>
        new(
            new FakeDecisionRepository(),
            new FakeSubmissionRepository());

    private static ClaimIssue CreateIssue(
        string id,
        ClaimId claimId) =>
        new()
        {
            Id = new ClaimIssueId(id),
            ClaimId = claimId,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

    private sealed class FakeDecisionRepository :
        IVaDecisionRepository
    {
        public Task<IReadOnlyList<IssueDecision>>
            GetIssueDecisionsAsync(
                ClaimIssueId id,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IssueDecision>>(
            [
                new()
                {
                    Id = new IssueDecisionId($"issue-decision-{id.Value}"),
                    VaDecisionId = new VaDecisionId($"decision-{id.Value}"),
                    ClaimIssueId = id,
                    Outcome = "Granted"
                }
            ]);

        public Task<VaDecision?> GetDecisionAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VaDecision?>(new()
            {
                Id = id,
                DecisionDate =
                    id.Value == "decision-issue-1"
                        ? new DateTimeOffset(
                            2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
                        : new DateTimeOffset(
                            2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });

        public Task<IReadOnlyList<SubmissionId>> GetSubmissionIdsAsync(
            IssueDecisionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubmissionId>>(
            [
                new SubmissionId($"submission-{id.Value}")
            ]);

        public Task AddDecisionAsync(
            VaDecision decision,
            IReadOnlyCollection<IssueDecision> issueDecisions,
            IReadOnlyCollection<IssueDecisionSubmission> associations,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IssueDecision>> GetIssueDecisionsAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddDecisionArtifactAsync(
            VaDecisionArtifact artifact,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ArtifactId>> GetArtifactIdsAsync(
            VaDecisionId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSubmissionRepository :
        ISubmissionRepository
    {
        public Task<Submission?> GetSubmissionAsync(
            SubmissionId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Submission?>(new()
            {
                Id = id,
                ClaimId = new ClaimId("claim-1"),
                SubmissionType = SubmissionTypes.InitialClaim
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

    private sealed class FakeClaimIssueRepository :
        IClaimIssueRepository
    {
        private readonly IReadOnlyList<ClaimIssue> _issues;

        public FakeClaimIssueRepository(
            params ClaimIssue[] issues) =>
            _issues = issues;

        public Task<IReadOnlyList<ClaimIssue>> GetClaimIssuesAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_issues);

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
