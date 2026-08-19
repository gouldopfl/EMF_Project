using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentPlanServiceTests
{
    [Fact]
    public async Task GetEvidenceDevelopmentPlanAsync_ComposesPlanDetails()
    {
        var planId = new EvidenceDevelopmentPlanId("plan-001");

        var plan = new EvidenceDevelopmentPlan
        {
            Id = planId,
            ClaimIssueId = new ClaimIssueId("issue-001"),
            Description = "Develop evidence."
        };

        var repository = new StubRepository(plan);

        var service =
            new EvidenceDevelopmentPlanService(repository);

        var result =
            await service.GetEvidenceDevelopmentPlanAsync(planId);

        Assert.NotNull(result);
        Assert.Equal(plan.Id, result!.Plan.Id);
        Assert.Single(result.Requirements);
        Assert.Single(result.EvidenceGaps);
        Assert.Single(result.Artifacts);
    }


    [Fact]
    public async Task GetEvidenceDevelopmentPlanAsync_ReturnsNullWhenPlanDoesNotExist()
    {
        var service =
            new EvidenceDevelopmentPlanService(
                new MissingPlanRepository());

        var result =
            await service.GetEvidenceDevelopmentPlanAsync(
                new EvidenceDevelopmentPlanId("missing-plan"));

        Assert.Null(result);
    }



    private sealed class MissingPlanRepository :
        IEvidenceDevelopmentPlanRepository
    {
        public Task<EvidenceDevelopmentPlan?>
            GetEvidenceDevelopmentPlanAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceDevelopmentPlan?>(null);

        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
            GetEvidenceDevelopmentPlanRequirementsAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken c = default) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken c = default) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
            GetEvidenceDevelopmentPlanArtifactsAsync(
                EvidenceDevelopmentPlanId id,
                CancellationToken c = default) =>
            throw new InvalidOperationException();

        public Task AddEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlan>> GetEvidenceDevelopmentPlansAsync(ClaimIssueId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanRequirementAsync(EvidenceDevelopmentPlanRequirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(EvidenceDevelopmentPlanEvidenceGap g, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanArtifactAsync(EvidenceDevelopmentPlanArtifact a, CancellationToken c = default) => throw new NotSupportedException();
    }


    private sealed class StubRepository :
        IEvidenceDevelopmentPlanRepository
    {
        private readonly EvidenceDevelopmentPlan _plan;

        public StubRepository(EvidenceDevelopmentPlan plan) =>
            _plan = plan;

        public Task<EvidenceDevelopmentPlan?>
            GetEvidenceDevelopmentPlanAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceDevelopmentPlan?>(_plan);

        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
            GetEvidenceDevelopmentPlanRequirementsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>(
                new[]
                {
                    new EvidenceDevelopmentPlanRequirement
                    {
                        EvidenceDevelopmentPlanId = planId,
                        RequirementId =
                            new RequirementId("requirement-001")
                    }
                });

        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>(
                new[]
                {
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId =
                            new EvidenceGapId("gap-001")
                    }
                });

        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
            GetEvidenceDevelopmentPlanArtifactsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>(
                new[]
                {
                    new EvidenceDevelopmentPlanArtifact
                    {
                        EvidenceDevelopmentPlanId = planId,
                        ArtifactId =
                            new ArtifactId("artifact-001"),
                        Role = "Supporting"
                    }
                });

        public Task AddEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlan>> GetEvidenceDevelopmentPlansAsync(ClaimIssueId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanRequirementAsync(EvidenceDevelopmentPlanRequirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(EvidenceDevelopmentPlanEvidenceGap g, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanArtifactAsync(EvidenceDevelopmentPlanArtifact a, CancellationToken c = default) => throw new NotSupportedException();
    }
}
