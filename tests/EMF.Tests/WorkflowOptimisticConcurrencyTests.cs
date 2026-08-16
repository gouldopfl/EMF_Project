using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowOptimisticConcurrencyTests
{
    [Fact]
    public async Task Stale_revision_cannot_update_or_append_transition()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("workflow-concurrency");

            var original =
                new WorkflowExecutionRecord
                {
                    WorkflowId = workflowId,
                    DefinitionId = "evidence-processing",
                    DefinitionVersion = "1",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    CurrentStatus = WorkflowStatus.Running,
                    RecoveryStatus = WorkflowRecoveryStatus.None
                };

            await repository.CreateExecutionAsync(original);

            await repository.UpdateExecutionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = workflowId,
                    DefinitionId = original.DefinitionId,
                    DefinitionVersion = original.DefinitionVersion,
                    CreatedUtc = original.CreatedUtc,
                    CurrentStatus = WorkflowStatus.Completed,
                    RecoveryStatus = WorkflowRecoveryStatus.None,
                    Revision = original.Revision
                });

            var exception =
                await Assert.ThrowsAsync<
                    WorkflowConcurrencyException>(
                        () => repository.UpdateExecutionAsync(
                            original));

            Assert.Equal(workflowId, exception.WorkflowId);
            Assert.Equal(0, exception.ExpectedRevision);

            await Assert.ThrowsAsync<
                WorkflowConcurrencyException>(
                    () => repository.ApplyStatusTransitionAsync(
                        original,
                        new WorkflowStatusTransition
                        {
                            WorkflowId = workflowId,
                            FromStatus = WorkflowStatus.Running,
                            ToStatus = WorkflowStatus.Failed,
                            RecordedUtc = DateTimeOffset.UtcNow
                        }));

            var stored =
                await repository.GetExecutionAsync(workflowId);

            Assert.NotNull(stored);
            Assert.Equal(
                WorkflowStatus.Completed,
                stored!.CurrentStatus);
            Assert.Equal(1, stored.Revision);

            var transitions =
                await repository.GetStatusTransitionsAsync(
                    workflowId);

            Assert.Empty(transitions);
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
