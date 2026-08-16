using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowAtomicTransitionPersistenceTests
{
    [Fact]
    public async Task ApplyStatusTransition_updates_execution_and_appends_history()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteWorkflowRepository(databasePath);
            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("workflow-atomic-transition");

            var createdUtc = DateTimeOffset.UtcNow;

            await repository.CreateExecutionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = workflowId,
                    DefinitionId = "evidence-processing",
                    DefinitionVersion = "1",
                    CreatedUtc = createdUtc,
                    CurrentStatus = WorkflowStatus.Running,
                    RecoveryStatus = WorkflowRecoveryStatus.None
                });

            var recordedUtc = DateTimeOffset.UtcNow;

            await repository.ApplyStatusTransitionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = workflowId,
                    DefinitionId = "evidence-processing",
                    DefinitionVersion = "1",
                    CreatedUtc = createdUtc,
                    CurrentStatus = WorkflowStatus.Completed,
                    RecoveryStatus = WorkflowRecoveryStatus.None
                },
                new WorkflowStatusTransition
                {
                    WorkflowId = workflowId,
                    FromStatus = WorkflowStatus.Running,
                    ToStatus = WorkflowStatus.Completed,
                    RecordedUtc = recordedUtc,
                    Message = "Workflow completed"
                });

            var execution =
                await repository.GetExecutionAsync(workflowId);

            var transitions =
                await repository.GetStatusTransitionsAsync(workflowId);

            Assert.NotNull(execution);
            Assert.Equal(
                WorkflowStatus.Completed,
                execution!.CurrentStatus);
            Assert.Equal(1, execution.Revision);

            var transition = Assert.Single(transitions);

            Assert.Equal(
                WorkflowStatus.Running,
                transition.FromStatus);

            Assert.Equal(
                WorkflowStatus.Completed,
                transition.ToStatus);

            Assert.Equal(
                recordedUtc,
                transition.RecordedUtc);

            Assert.Equal(
                "Workflow completed",
                transition.Message);
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
    public async Task ApplyStatusTransition_rejects_mismatched_workflow_ids()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteWorkflowRepository(databasePath);
            await repository.InitializeAsync();

            var execution =
                new WorkflowExecutionRecord
                {
                    WorkflowId = new WorkflowId("workflow-a"),
                    DefinitionId = "evidence-processing",
                    DefinitionVersion = "1",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    CurrentStatus = WorkflowStatus.Completed,
                    RecoveryStatus = WorkflowRecoveryStatus.None
                };

            var transition =
                new WorkflowStatusTransition
                {
                    WorkflowId = new WorkflowId("workflow-b"),
                    FromStatus = WorkflowStatus.Running,
                    ToStatus = WorkflowStatus.Completed,
                    RecordedUtc = DateTimeOffset.UtcNow
                };

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.ApplyStatusTransitionAsync(
                    execution,
                    transition));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
