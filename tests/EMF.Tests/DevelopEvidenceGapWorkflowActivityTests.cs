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

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                gap.Id);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-1")
                });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenGapIsMissing()
    {
        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(null),
                new FakeGuidanceRepository(),
                new EvidenceGapId("gap-1"));

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-1")
                });

        Assert.False(result.Succeeded);
    }


    private sealed class FakeGuidanceRepository :
        IEvidenceRequirementGuidanceRepository
    {
        public Task<IReadOnlyList<EvidenceRequirementGuidance>>
            GetEvidenceRequirementGuidanceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EvidenceRequirementGuidance>>(
                Array.Empty<EvidenceRequirementGuidance>());

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
