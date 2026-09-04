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
        Assert.Equal(
            EvidenceDevelopmentPlanStatuses.Unknown,
            result!.Status!.Status);
        Assert.Equal(plan.Id, result.Plan.Id);
        Assert.Single(result.Requirements);
        Assert.Single(result.EvidenceGaps);
        Assert.Single(result.Artifacts);
        Assert.Single(result.Executions);
        Assert.Single(result.Results);
        Assert.Equal(
            new EvidenceDevelopmentPlanId("plan-001"),
            result.Executions[0].EvidenceDevelopmentPlanId);
        Assert.Equal(
            new EvidenceGapId("gap-001"),
            result.Executions[0].EvidenceGapId);
        Assert.Equal(
            new EvidenceGapId("gap-001"),
            result.Results[0].EvidenceGapId);
    }


    [Fact]
    public async Task GetEvidenceDevelopmentPlanAsync_IncludesGapDetails()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-gap-details");

        var gap =
            new EvidenceGap
            {
                Id = new EvidenceGapId("gap-001"),
                ClaimIssueId = new ClaimIssueId("issue-001"),
                RequirementId =
                    new RequirementId("requirement-001"),
                Description = "Resolved evidence gap.",
                Status = EvidenceGapStatuses.Resolved
            };

        var service =
            new EvidenceDevelopmentPlanService(
                new StubRepository(
                    new EvidenceDevelopmentPlan
                    {
                        Id = planId,
                        ClaimIssueId = gap.ClaimIssueId,
                        Description = "Develop evidence."
                    }),
                new StubGapRepository(gap));

        var result =
            await service.GetEvidenceDevelopmentPlanAsync(planId);

        Assert.NotNull(result);

        Assert.Equal(
            EvidenceDevelopmentPlanStatuses.Complete,
            result!.Status!.Status);

        var detail =
            Assert.Single(result.GapDetails);

        Assert.Equal(gap.Id, detail.Id);
        Assert.Equal(
            EvidenceGapStatuses.Resolved,
            detail.Status);
    }

    [Fact]
    public async Task GetEvidenceDevelopmentPlansAsync_ReturnsPlansForClaimIssue()
    {
        var issueId = new ClaimIssueId("issue-001");
        var planId = new EvidenceDevelopmentPlanId("plan-001");

        var repository = new StubRepository(
            new EvidenceDevelopmentPlan
            {
                Id = planId,
                ClaimIssueId = issueId,
                Description = "Develop evidence."
            });

        var service =
            new EvidenceDevelopmentPlanService(repository);

        var result =
            await service.GetEvidenceDevelopmentPlansAsync(issueId);

        var plan = Assert.Single(result);

        Assert.Equal(planId, plan.Id);
        Assert.Equal(issueId, plan.ClaimIssueId);
    }

    [Fact]
    public async Task GetEvidenceDevelopmentPlanAsync_RejectsDifferentReturnedPlan()
    {
        var requestedPlanId =
            new EvidenceDevelopmentPlanId("plan-requested");

        var returnedPlanId =
            new EvidenceDevelopmentPlanId("plan-returned");

        var service =
            new EvidenceDevelopmentPlanService(
                new StubRepository(
                    new EvidenceDevelopmentPlan
                    {
                        Id = returnedPlanId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-001"),
                        Description = "Unexpected plan."
                    }));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.GetEvidenceDevelopmentPlanAsync(
                        requestedPlanId));

        Assert.Contains(
            requestedPlanId.Value,
            exception.Message);

        Assert.Contains(
            returnedPlanId.Value,
            exception.Message);
    }


    [Fact]
    public async Task GetEvidenceDevelopmentPlanAsync_RejectsEvidenceGapForDifferentPlan()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-requested");

        var otherPlanId =
            new EvidenceDevelopmentPlanId("plan-other");

        var service =
            new EvidenceDevelopmentPlanService(
                new StubRepository(
                    new EvidenceDevelopmentPlan
                    {
                        Id = planId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-001"),
                        Description = "Develop evidence."
                    },
                    otherPlanId));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.GetEvidenceDevelopmentPlanAsync(
                        planId));

        Assert.Contains(
            planId.Value,
            exception.Message);

        Assert.Contains(
            otherPlanId.Value,
            exception.Message);
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




    [Fact]
    public async Task CreateEvidenceDevelopmentPlanAsync_PersistsPlanAndGapLinks()
    {
        var repository = new RecordingRepository();

        var service =
            new EvidenceDevelopmentPlanService(repository);

        var request =
            new CreateEvidenceDevelopmentPlanRequest
            {
                PlanId =
                    new EvidenceDevelopmentPlanId("plan-created-001"),
                ClaimIssueId =
                    new ClaimIssueId("issue-created-001"),
                Description =
                    "Develop identified evidence gaps.",
                EvidenceGapIds =
                    new[]
                    {
                        new EvidenceGapId("gap-001"),
                        new EvidenceGapId("gap-002")
                    }
            };

        var result =
            await service.CreateEvidenceDevelopmentPlanAsync(
                request);

        Assert.Equal(
            request.PlanId,
            result.Plan.Id);

        Assert.Equal(
            request.ClaimIssueId,
            result.Plan.ClaimIssueId);

        Assert.Equal(
            2,
            result.EvidenceGaps.Count);
    }



    private sealed class RecordingRepository :
        IEvidenceDevelopmentPlanRepository
    {
        private EvidenceDevelopmentPlan? _plan;

        private readonly List<EvidenceDevelopmentPlanEvidenceGap>
            _evidenceGaps = new();

        public Task CreateEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> gaps,
            CancellationToken cancellationToken = default)
        {
            _plan = plan;
            _evidenceGaps.AddRange(gaps);
            return Task.CompletedTask;
        }

        public Task AddEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            CancellationToken cancellationToken = default)
        {
            _plan = plan;
            return Task.CompletedTask;
        }

        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(
            EvidenceDevelopmentPlanEvidenceGap evidenceGap,
            CancellationToken cancellationToken = default)
        {
            _evidenceGaps.Add(evidenceGap);
            return Task.CompletedTask;
        }

        public Task<EvidenceDevelopmentPlan?>
            GetEvidenceDevelopmentPlanAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(_plan);

        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>(
                _evidenceGaps.ToArray());

        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
            GetEvidenceDevelopmentPlanRequirementsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>(
                Array.Empty<EvidenceDevelopmentPlanRequirement>());

        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
            GetEvidenceDevelopmentPlanArtifactsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>(
                Array.Empty<EvidenceDevelopmentPlanArtifact>());

        public Task<EvidenceDevelopmentExecution?>
            GetEvidenceDevelopmentExecutionAsync(
                EvidenceDevelopmentPlanId planId,
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceDevelopmentExecution?>(
                new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId = planId,
                    EvidenceGapId = evidenceGapId,
                    WorkflowId =
                        new EMF.Core.Models.Identities.WorkflowId(
                            "workflow-001")
                });

        public Task<EvidenceDevelopmentResult?>
            GetEvidenceDevelopmentResultAsync(
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceDevelopmentResult?>(
                new EvidenceDevelopmentResult
                {
                    EvidenceGapId = evidenceGapId,
                    RequirementId =
                        new RequirementId("requirement-001"),
                    EvidenceGuidance = []
                });

        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanRequirementAsync(
            EvidenceDevelopmentPlanRequirement requirement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanArtifactAsync(
            EvidenceDevelopmentPlanArtifact artifact,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

        public Task CreateEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> gaps,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId id,
                CancellationToken c = default) =>
            throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanRequirementAsync(EvidenceDevelopmentPlanRequirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(EvidenceDevelopmentPlanEvidenceGap g, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanArtifactAsync(EvidenceDevelopmentPlanArtifact a, CancellationToken c = default) => throw new NotSupportedException();
    }


    private sealed class StubGapRepository :
        IEvidenceGapRepository
    {
        private readonly EvidenceGap _gap;

        public StubGapRepository(EvidenceGap gap)
        {
            _gap = gap;
        }

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceGap?>(_gap);

        public Task AddEvidenceGapAsync(
            EvidenceGap gap,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            RequirementId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubRepository :
        IEvidenceDevelopmentPlanRepository
    {
        private readonly EvidenceDevelopmentPlan _plan;
        private readonly EvidenceDevelopmentPlanId? _evidenceGapPlanId;

        public StubRepository(
            EvidenceDevelopmentPlan plan,
            EvidenceDevelopmentPlanId? evidenceGapPlanId = null)
        {
            _plan = plan;
            _evidenceGapPlanId = evidenceGapPlanId;
        }

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
                        EvidenceDevelopmentPlanId =
                            _evidenceGapPlanId ?? planId,
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

        public Task<EvidenceDevelopmentExecution?>
            GetEvidenceDevelopmentExecutionAsync(
                EvidenceDevelopmentPlanId planId,
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceDevelopmentExecution?>(
                new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId = planId,
                    EvidenceGapId = evidenceGapId,
                    WorkflowId =
                        new EMF.Core.Models.Identities.WorkflowId(
                            "workflow-001")
                });

        public Task<EvidenceDevelopmentResult?>
            GetEvidenceDevelopmentResultAsync(
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidenceDevelopmentResult?>(
                new EvidenceDevelopmentResult
                {
                    EvidenceGapId = evidenceGapId,
                    RequirementId =
                        new RequirementId("requirement-001"),
                    EvidenceGuidance = []
                });

        public Task CreateEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> gaps,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId id,
                CancellationToken c = default) =>
            Task.FromResult<IReadOnlyList<EvidenceDevelopmentPlan>>(
                new[] { _plan });
        public Task AddEvidenceDevelopmentPlanRequirementAsync(EvidenceDevelopmentPlanRequirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(EvidenceDevelopmentPlanEvidenceGap g, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanArtifactAsync(EvidenceDevelopmentPlanArtifact a, CancellationToken c = default) => throw new NotSupportedException();
    }
}
