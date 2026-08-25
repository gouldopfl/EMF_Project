using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimEvidenceDetailsServiceTests
{
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
