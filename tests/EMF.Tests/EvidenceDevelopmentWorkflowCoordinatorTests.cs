using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Contracts;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentWorkflowCoordinatorTests
{
    [Fact]
    public async Task StartAsync_PersistsWorkflowLink()
    {
        var workflow = new FakeWorkflowService();
        var repository = new FakeRepository();
        var runner = new FakeWorkflowRunner();

        var coordinator =
            new EvidenceDevelopmentWorkflowCoordinator(
                workflow,
                repository,
                runner,
                new FakeGapRepository());

        var result = await coordinator.StartAsync(
            new EvidenceDevelopmentPlanId("plan-1"),
            new EvidenceGapId("gap-1"));

        Assert.NotNull(repository.Execution);
        Assert.Equal(result.WorkflowId, repository.Execution!.WorkflowId);
        Assert.Equal(result.WorkflowId, runner.WorkflowId);
    }


    [Fact]
    public async Task StartAsync_FailsWorkflowWhenLinkPersistenceFails()
    {
        var workflow = new FakeWorkflowService();
        var repository = new FailingRepository();

        var coordinator =
            new EvidenceDevelopmentWorkflowCoordinator(
                workflow,
                repository,
                new FakeWorkflowRunner(),
                new FakeGapRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1")));

        Assert.Equal(
            new WorkflowId("workflow-1"),
            workflow.FailedWorkflowId);
    }


    private sealed class FakeWorkflowRunner : IWorkflowRunner
    {
        public WorkflowId? WorkflowId { get; private set; }

        public Task ExecuteAsync(
            EMF.Orchestration.Models.WorkflowExecutionContext context,
            IEnumerable<IWorkflowActivity> activities,
            string? retryActivityId = null,
            OperationId? retryOperationId = null,
            CancellationToken cancellationToken = default)
        {
            WorkflowId = context.WorkflowId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGapRepository : IEvidenceGapRepository
    {
        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId id,
            CancellationToken cancellationToken = default)
            => Task.FromResult<EvidenceGap?>(null);

        public Task AddEvidenceGapAsync(EvidenceGap gap, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(ClaimIssueId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(RequirementId id, CancellationToken c = default) => throw new NotSupportedException();
    }

    private sealed class FakeWorkflowService : IWorkflowService
    {
        public WorkflowId? FailedWorkflowId { get; private set; }

        public Task<WorkflowId> StartAsync(
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowId("workflow-1"));

        public Task RecordCheckpointAsync(WorkflowCheckpoint x, CancellationToken c = default) => throw new NotSupportedException();
        public Task<WorkflowOperationRecord?> GetOperationAsync(WorkflowId w, string a, OperationId o, CancellationToken c = default) => throw new NotSupportedException();
        public Task<bool> TryCreateOperationAsync(WorkflowOperationRecord o, CancellationToken c = default) => throw new NotSupportedException();
        public Task UpdateOperationAsync(WorkflowOperationRecord o, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(WorkflowId w, CancellationToken c = default) => throw new NotSupportedException();
        public Task CompleteAsync(WorkflowId w, CancellationToken c = default) => throw new NotSupportedException();
        public Task FailAsync(WorkflowId w, string m, CancellationToken c = default)
        {
            FailedWorkflowId = w;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRepository : FakeRepository
    {
        public override Task AddEvidenceDevelopmentExecutionAsync(
            EvidenceDevelopmentExecution execution,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Persistence failed.");
    }

    private class FakeRepository : IEvidenceDevelopmentPlanRepository
    {
        public EvidenceDevelopmentExecution? Execution { get; private set; }

        public virtual Task AddEvidenceDevelopmentExecutionAsync(
            EvidenceDevelopmentExecution execution,
            CancellationToken cancellationToken = default)
        {
            Execution = execution;
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
}
