using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimEvidenceDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_RejectsReturnedDifferentClaim()
    {
        var requestedId =
            new ClaimId("claim-requested");

        var returnedClaim =
            new Claim
            {
                Id = new ClaimId("claim-other"),
                VeteranId = new VeteranId("veteran-1")
            };

        var service =
            new ClaimEvidenceDetailsService(
                new FakeClaimRepository(returnedClaim),
                new FakeClaimIssueRepository(),
                new FakeEvidenceDetailsService());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(requestedId));

        Assert.Equal(
            "Claim lookup returned a different claim.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsIssueForDifferentClaim()
    {
        var claimId =
            new ClaimId("claim-1");

        var claim =
            new Claim
            {
                Id = claimId,
                VeteranId = new VeteranId("veteran-1")
            };

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = new ClaimId("claim-other"),
                ClaimIssueType = "ServiceConnection"
            };

        var service =
            new ClaimEvidenceDetailsService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeEvidenceDetailsService());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(claimId));

        Assert.Equal(
            "Claim lookup returned an issue for a different claim.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsEvidenceForDifferentIssue()
    {
        var claimId = new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var otherIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-other"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var evidence =
            new ClaimIssueEvidenceDetails
            {
                ClaimIssue = otherIssue,
                Checklist =
                    new ClaimIssueEvidenceChecklist
                    {
                        ClaimIssueId = otherIssue.Id,
                        RequirementChecklists = []
                    },
                DevelopmentPlans = []
            };

        var service =
            new ClaimEvidenceDetailsService(
                new FakeClaimRepository(
                    new Claim
                    {
                        Id = claimId,
                        VeteranId = new VeteranId("veteran-1")
                    }),
                new FakeClaimIssueRepository(issue),
                new FixedEvidenceDetailsService(evidence));

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(claimId));

        Assert.Equal(
            "Claim issue evidence identity mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsEvidenceForDifferentClaim()
    {
        var claimId = new ClaimId("claim-1");

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claimId,
                ClaimIssueType = "ServiceConnection"
            };

        var evidenceIssue =
            new ClaimIssue
            {
                Id = issue.Id,
                ClaimId = new ClaimId("claim-other"),
                ClaimIssueType = "ServiceConnection"
            };

        var evidence =
            new ClaimIssueEvidenceDetails
            {
                ClaimIssue = evidenceIssue,
                Checklist =
                    new ClaimIssueEvidenceChecklist
                    {
                        ClaimIssueId = evidenceIssue.Id,
                        RequirementChecklists = []
                    },
                DevelopmentPlans = []
            };

        var service =
            new ClaimEvidenceDetailsService(
                new FakeClaimRepository(
                    new Claim
                    {
                        Id = claimId,
                        VeteranId = new VeteranId("veteran-1")
                    }),
                new FakeClaimIssueRepository(issue),
                new FixedEvidenceDetailsService(evidence));

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(claimId));

        Assert.Equal(
            "Claim issue evidence claim ownership mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_ComposesIssueEvidenceDetails()
    {
        var claimId = new ClaimId("claim-001");
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-001"),
            ClaimId = claimId,
            ClaimIssueType = "ServiceConnection"
        };

        var claim = new Claim
        {
            Id = claimId,
            VeteranId = new VeteranId("veteran-001")
        };

        var evidence =
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
            };

        var service =
            new ClaimEvidenceDetailsService(
                new FakeClaimRepository(claim),
                new FakeClaimIssueRepository(issue),
                new FakeEvidenceDetailsService(evidence));

        var result = await service.GetAsync(claimId);

        Assert.NotNull(result);
        Assert.Equal(claimId, result!.Claim.Id);
        Assert.Single(result.Issues);
        Assert.Equal(issue.Id, result.Issues[0].ClaimIssue.Id);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenClaimDoesNotExist()
    {
        var service =
            new ClaimEvidenceDetailsService(
                new FakeClaimRepository(),
                new FakeClaimIssueRepository(),
                new FakeEvidenceDetailsService());

        var result =
            await service.GetAsync(new ClaimId("missing"));

        Assert.Null(result);
    }

    private sealed class FakeClaimRepository : IClaimRepository
    {
        private readonly Claim? _claim;

        public FakeClaimRepository(Claim? claim = null) =>
            _claim = claim;

        public Task<Claim?> GetClaimAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_claim);

        public Task<IReadOnlyList<Claim>> GetClaimsAsync(
            VeteranId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimAsync(
            Claim claim,
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

        public Task<IReadOnlyList<ClaimIssue>>
            GetClaimIssuesAsync(
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

    private sealed class FixedEvidenceDetailsService :
        IClaimIssueEvidenceDetailsService
    {
        private readonly ClaimIssueEvidenceDetails _details;

        public FixedEvidenceDetailsService(
            ClaimIssueEvidenceDetails details) =>
            _details = details;

        public Task<ClaimIssueEvidenceDetails?> GetAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaimIssueEvidenceDetails?>(_details);
    }

    private sealed class FakeEvidenceDetailsService :
        IClaimIssueEvidenceDetailsService
    {
        private readonly IReadOnlyList<ClaimIssueEvidenceDetails> _details;

        public FakeEvidenceDetailsService(
            params ClaimIssueEvidenceDetails[] details) =>
            _details = details;

        public Task<ClaimIssueEvidenceDetails?> GetAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _details.FirstOrDefault(
                    x => x.ClaimIssue.Id == id));
    }
}
