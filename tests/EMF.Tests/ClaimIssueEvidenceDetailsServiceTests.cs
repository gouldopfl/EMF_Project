using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueEvidenceDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_ComposesEvidenceDetails()
    {
        var issueId = new ClaimIssueId("issue-001");
        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "ServiceConnection"
        };

        var checklist = new ClaimIssueEvidenceChecklist
        {
            ClaimIssueId = issueId,
            RequirementChecklists = []
        };

        var plans =
            new[]
            {
                new EvidenceDevelopmentPlan
                {
                    Id = new EvidenceDevelopmentPlanId("plan-001"),
                    ClaimIssueId = issueId,
                    Description = "Develop evidence."
                }
            };

        var service =
            new ClaimIssueEvidenceDetailsService(
                new FakeIssueRepository(issue),
                new FakeChecklistService(checklist),
                new FakePlanService(plans));

        var result =
            await service.GetAsync(issueId);

        Assert.NotNull(result);
        Assert.Equal(issueId, result!.ClaimIssue.Id);
        Assert.Equal(issueId, result.Checklist.ClaimIssueId);
        Assert.Single(result.DevelopmentPlans);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenIssueDoesNotExist()
    {
        var service =
            new ClaimIssueEvidenceDetailsService(
                new FakeIssueRepository(null),
                new FakeChecklistService(
                    new ClaimIssueEvidenceChecklist
                    {
                        ClaimIssueId =
                            new ClaimIssueId("unused"),
                        RequirementChecklists = []
                    }),
                new FakePlanService([]));

        var result =
            await service.GetAsync(
                new ClaimIssueId("missing"));

        Assert.Null(result);
    }

    private sealed class FakeIssueRepository :
        IClaimIssueRepository
    {
        private readonly ClaimIssue? _issue;

        public FakeIssueRepository(ClaimIssue? issue) =>
            _issue = issue;

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_issue);

        public Task<IReadOnlyList<ClaimIssue>> GetClaimIssuesAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeChecklistService :
        IClaimIssueEvidenceChecklistService
    {
        private readonly ClaimIssueEvidenceChecklist _checklist;

        public FakeChecklistService(
            ClaimIssueEvidenceChecklist checklist) =>
            _checklist = checklist;

        public Task<ClaimIssueEvidenceChecklist> CreateChecklistAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_checklist);
    }

    private sealed class FakePlanService :
        IEvidenceDevelopmentPlanService
    {
        private readonly IReadOnlyList<EvidenceDevelopmentPlan> _plans;

        public FakePlanService(
            IReadOnlyList<EvidenceDevelopmentPlan> plans) =>
            _plans = plans;

        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId id,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_plans);

        public Task<EvidenceDevelopmentPlanDetails>
            CreateEvidenceDevelopmentPlanAsync(
                CreateEvidenceDevelopmentPlanRequest request,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceDevelopmentPlanDetails?>
            GetEvidenceDevelopmentPlanAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
