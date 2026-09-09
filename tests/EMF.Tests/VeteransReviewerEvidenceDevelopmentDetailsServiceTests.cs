using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerEvidenceDevelopmentDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsPersistedResult()
    {
        var f = Fixture.Create();

        var result =
            await f.Service.GetAsync(f.IssueId);

        var item = Assert.Single(result);

        Assert.Equal(f.Gap.Id, item.Gap.Id);
        Assert.Equal(
            f.Gap.RequirementId,
            item.Result.RequirementId);
    }

    [Fact]
    public async Task GetAsync_RejectsWrongClaimIssue()
    {
        var f = Fixture.Create();

        f.GapRepository.Gap =
            new EvidenceGap
            {
                Id = f.Gap.Id,
                ClaimIssueId =
                    new ClaimIssueId("wrong-issue"),
                RequirementId = f.Gap.RequirementId,
                Description = "Wrong claim"
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Service.GetAsync(f.IssueId));
    }

    [Fact]
    public async Task GetAsync_RejectsResultRequirementMismatch()
    {
        var f = Fixture.Create();

        var repository =
            new FakeDevelopmentRepository(
                new EvidenceDevelopmentPlan
                {
                    Id = new EvidenceDevelopmentPlanId("plan-1"),
                    ClaimIssueId = f.IssueId,
                    Description = "Develop evidence"
                },
                new EvidenceDevelopmentPlanEvidenceGap
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-1"),
                    EvidenceGapId = f.Gap.Id
                },
                new EvidenceDevelopmentResult
                {
                    EvidenceGapId = f.Gap.Id,
                    RequirementId =
                        new RequirementId("wrong-requirement"),
                    EvidenceGuidance = []
                });

        var service =
            new VeteransReviewerEvidenceDevelopmentDetailsService(
                repository,
                f.GapRepository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(f.IssueId));
    }

    [Fact]
    public async Task GetAsync_SkipsGapWithoutDevelopmentResult()
    {
        var f = Fixture.Create();

        var repository =
            new FakeDevelopmentRepository(
                new EvidenceDevelopmentPlan
                {
                    Id = new EvidenceDevelopmentPlanId("plan-1"),
                    ClaimIssueId = f.IssueId,
                    Description = "Develop evidence"
                },
                new EvidenceDevelopmentPlanEvidenceGap
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-1"),
                    EvidenceGapId = f.Gap.Id
                },
                null);

        var service =
            new VeteransReviewerEvidenceDevelopmentDetailsService(
                repository,
                f.GapRepository);

        var result =
            await service.GetAsync(f.IssueId);

        Assert.Empty(result);
    }

    private sealed class Fixture
    {
        public required ClaimIssueId IssueId { get; init; }
        public required EvidenceGap Gap { get; init; }
        public required FakeDevelopmentRepository DevelopmentRepository { get; init; }
        public required FakeGapRepository GapRepository { get; init; }

        public VeteransReviewerEvidenceDevelopmentDetailsService Service =>
            new(DevelopmentRepository, GapRepository);

        public static Fixture Create()
        {
            var issueId = new ClaimIssueId("issue-1");
            var requirementId = new RequirementId("req-1");
            var gapId = new EvidenceGapId("gap-1");
            var planId = new EvidenceDevelopmentPlanId("plan-1");

            var gap =
                new EvidenceGap
                {
                    Id = gapId,
                    ClaimIssueId = issueId,
                    RequirementId = requirementId,
                    Description = "Missing nexus evidence"
                };

            return new Fixture
            {
                IssueId = issueId,
                Gap = gap,
                DevelopmentRepository =
                    new FakeDevelopmentRepository(
                        new EvidenceDevelopmentPlan
                        {
                            Id = planId,
                            ClaimIssueId = issueId,
                            Description = "Develop evidence"
                        },
                        new EvidenceDevelopmentPlanEvidenceGap
                        {
                            EvidenceDevelopmentPlanId = planId,
                            EvidenceGapId = gapId
                        },
                        new EvidenceDevelopmentResult
                        {
                            EvidenceGapId = gapId,
                            RequirementId = requirementId,
                            EvidenceGuidance = []
                        }),
                GapRepository =
                    new FakeGapRepository { Gap = gap }
            };
        }
    }

    private sealed class FakeDevelopmentRepository :
        IEvidenceDevelopmentPlanRepository
    {
        private readonly EvidenceDevelopmentPlan _plan;
        private readonly EvidenceDevelopmentPlanEvidenceGap _link;
        private readonly EvidenceDevelopmentResult? _result;

        public FakeDevelopmentRepository(
            EvidenceDevelopmentPlan plan,
            EvidenceDevelopmentPlanEvidenceGap link,
            EvidenceDevelopmentResult? result)
        {
            _plan = plan;
            _link = link;
            _result = result;
        }

        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId id,
                CancellationToken c = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlan>>(
                [_plan]);

        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken c = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>(
                [_link]);

        public Task<EvidenceDevelopmentResult?>
            GetEvidenceDevelopmentResultAsync(
                EvidenceGapId id,
                CancellationToken c = default) =>
            Task.FromResult(_result);

        public Task CreateEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan p,
            IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> g,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan p,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<EvidenceDevelopmentPlan?> GetEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlanId id,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanArtifactAsync(
            EvidenceDevelopmentPlanArtifact a,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
            GetEvidenceDevelopmentPlanArtifactsAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(
            EvidenceDevelopmentPlanEvidenceGap g,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanRequirementAsync(
            EvidenceDevelopmentPlanRequirement r,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
            GetEvidenceDevelopmentPlanRequirementsAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken c = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGapRepository :
        IEvidenceGapRepository
    {
        public EvidenceGap? Gap { get; set; }

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId id,
            CancellationToken c = default) =>
            Task.FromResult(Gap);

        public Task AddEvidenceGapAsync(
            EvidenceGap g,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            ClaimIssueId id,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            RequirementId id,
            CancellationToken c = default) =>
            throw new NotSupportedException();
    }
}
