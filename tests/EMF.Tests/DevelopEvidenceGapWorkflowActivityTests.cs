using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Models;

namespace EMF.Tests;

public sealed class DevelopEvidenceGapWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_SucceedsWhenGapExists()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-1"),
            ClaimIssueId = new ClaimIssueId("issue-1"),
            RequirementId = new RequirementId("req-1"),
            Description = "Missing evidence."
        };

        var development = new FakeDevelopmentRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                development,
                gap.Id);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-1")
                });

        Assert.True(result.Succeeded);
        Assert.NotNull(development.Result);
        Assert.Equal(gap.Id, development.Result!.EvidenceGapId);
        Assert.Equal(gap.RequirementId, development.Result.RequirementId);
    }


    [Fact]
    public async Task ExecuteAsync_LoadsGuidanceForGapRequirement()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-guidance-1"),
            ClaimIssueId = new ClaimIssueId("issue-guidance-1"),
            RequirementId = new RequirementId("req-guidance-1"),
            Description = "Missing evidence."
        };

        var guidance = new FakeGuidanceRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                guidance,
                new FakeDevelopmentRepository(),
                gap.Id);

        await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-guidance-1")
            });

        Assert.Equal(
            gap.RequirementId,
            guidance.RequestedRequirementId);
    }


    [Fact]
    public async Task ExecuteAsync_PropagatesResultPersistenceFailure()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-fail-1"),
            ClaimIssueId = new ClaimIssueId("issue-fail-1"),
            RequirementId = new RequirementId("req-fail-1"),
            Description = "Missing evidence."
        };

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                new FailingDevelopmentRepository(),
                gap.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-fail-1")
                }));
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenGapIsMissing()
    {
        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(null),
                new FakeGuidanceRepository(),
                new FakeDevelopmentRepository(),
                new EvidenceGapId("gap-1"));

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-1")
                });

        Assert.False(result.Succeeded);
    }



    private sealed class FailingDevelopmentRepository :
        FakeDevelopmentRepository
    {
        public override Task AddEvidenceDevelopmentResultAsync(
            EvidenceDevelopmentResult result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Persistence failed.");
    }

    private class FakeDevelopmentRepository :
        IEvidenceDevelopmentPlanRepository
    {
        public EvidenceDevelopmentResult? Result { get; private set; }

        public virtual Task AddEvidenceDevelopmentResultAsync(
            EvidenceDevelopmentResult result,
            CancellationToken cancellationToken = default)
        {
            Result = result;
            return Task.CompletedTask;
        }

        public Task CreateEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> g, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<EvidenceDevelopmentPlan?> GetEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanArtifactAsync(EvidenceDevelopmentPlanArtifact a, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>> GetEvidenceDevelopmentPlanArtifactsAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(EvidenceDevelopmentPlanEvidenceGap g, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>> GetEvidenceDevelopmentPlanEvidenceGapsAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanRequirementAsync(EvidenceDevelopmentPlanRequirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>> GetEvidenceDevelopmentPlanRequirementsAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlan>> GetEvidenceDevelopmentPlansAsync(ClaimIssueId c, CancellationToken t = default) => throw new NotSupportedException();
    }

    private sealed class FakeGuidanceRepository :
        IEvidenceRequirementGuidanceRepository
    {
        public RequirementId? RequestedRequirementId { get; private set; }

        public Task<IReadOnlyList<EvidenceRequirementGuidance>>
            GetEvidenceRequirementGuidanceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            RequestedRequirementId = requirementId;

            return Task.FromResult<IReadOnlyList<EvidenceRequirementGuidance>>(
                Array.Empty<EvidenceRequirementGuidance>());
        }

        public Task AddEvidenceRequirementGuidanceAsync(
            EvidenceRequirementGuidance guidance,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceRequirementGuidance?>
            GetEvidenceRequirementGuidanceAsync(
                EvidenceRequirementGuidanceId guidanceId,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRepository : IEvidenceGapRepository
    {
        private readonly EvidenceGap? _gap;

        public FakeRepository(EvidenceGap? gap)
        {
            _gap = gap;
        }

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_gap);

        public Task AddEvidenceGapAsync(EvidenceGap gap, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(ClaimIssueId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(RequirementId id, CancellationToken c = default) => throw new NotSupportedException();
    }
}
