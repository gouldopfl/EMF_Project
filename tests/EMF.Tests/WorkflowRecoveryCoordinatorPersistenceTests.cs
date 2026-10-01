using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowRecoveryCoordinatorPersistenceTests
{
    [Fact]
    public async Task Stale_revision_records_neither_recovery_update_nor_decision()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteWorkflowRepository(databasePath);
            await repository.InitializeAsync();

            var workflowId = new WorkflowId("workflow-stale-recovery");
            var createdUtc = DateTimeOffset.UtcNow;

            await repository.CreateExecutionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = workflowId,
                    DefinitionId = "test",
                    DefinitionVersion = "1",
                    CreatedUtc = createdUtc,
                    CurrentStatus = WorkflowStatus.Interrupted,
                    RecoveryStatus = WorkflowRecoveryStatus.None,
                    Revision = 0
                });

            var coordinator = new WorkflowRecoveryCoordinator(
                repository,
                new ConcurrentUpdateRecoveryPolicy(repository));

            var definition = new WorkflowDefinition
            {
                Id = "test",
                Name = "Test Workflow",
                Version = "1",
                ActivityIds = Array.Empty<string>()
            };

            await Assert.ThrowsAsync<WorkflowConcurrencyException>(
                () => coordinator.RecoverAsync(workflowId, definition));

            var persisted = await repository.GetExecutionAsync(workflowId);
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted!.Revision);
            Assert.Equal(
                WorkflowRecoveryStatus.None,
                persisted.RecoveryStatus);

            var decisions =
                await repository.GetRecoveryDecisionsAsync(workflowId);

            Assert.Empty(decisions);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Decision_insert_failure_rolls_back_execution_update()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteWorkflowRepository(databasePath);
            await repository.InitializeAsync();

            var workflowId = new WorkflowId("workflow-recovery-rollback");
            var createdUtc = DateTimeOffset.UtcNow;

            await repository.CreateExecutionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = workflowId,
                    DefinitionId = "test",
                    DefinitionVersion = "1",
                    CreatedUtc = createdUtc,
                    CurrentStatus = WorkflowStatus.Interrupted,
                    RecoveryStatus = WorkflowRecoveryStatus.None,
                    Revision = 0
                });

            var updatedExecution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = createdUtc,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.Recoverable,
                Revision = 0
            };

            var invalidDecision = new WorkflowRecoveryDecisionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = null!,
                DefinitionVersion = "1",
                EvaluatedRevision = 0,
                Decision = RecoveryDecision.Resume,
                RecordedUtc = DateTimeOffset.UtcNow
            };

            await Assert.ThrowsAnyAsync<Exception>(
                () => repository.ApplyRecoveryDecisionAsync(
                    updatedExecution,
                    invalidDecision));

            var persisted = await repository.GetExecutionAsync(workflowId);
            Assert.NotNull(persisted);
            Assert.Equal(0, persisted!.Revision);
            Assert.Equal(
                WorkflowRecoveryStatus.None,
                persisted.RecoveryStatus);

            var decisions =
                await repository.GetRecoveryDecisionsAsync(workflowId);

            Assert.Empty(decisions);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private sealed class ConcurrentUpdateRecoveryPolicy : IWorkflowRecoveryPolicy
    {
        private readonly SqliteWorkflowRepository _repository;

        public ConcurrentUpdateRecoveryPolicy(
            SqliteWorkflowRepository repository)
        {
            _repository = repository;
        }

        public async Task<RecoveryDecision> EvaluateAsync(
            WorkflowExecutionRecord execution,
            WorkflowDefinition definition,
            IReadOnlyList<WorkflowCheckpoint> checkpoints,
            IReadOnlyList<WorkflowOperationRecord> operations,
            CancellationToken cancellationToken = default)
        {
            await _repository.UpdateExecutionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = execution.WorkflowId,
                    DefinitionId = execution.DefinitionId,
                    DefinitionVersion = execution.DefinitionVersion,
                    CreatedUtc = execution.CreatedUtc,
                    CurrentStatus = execution.CurrentStatus,
                    RecoveryStatus = execution.RecoveryStatus,
                    Revision = execution.Revision
                },
                cancellationToken);

            return RecoveryDecision.Resume;
        }
    }
}
