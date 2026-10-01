using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowRecoveryDecisionPersistenceTests
{
    [Fact]
    public async Task SqliteWorkflowRepository_RecoveryDecisionRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("workflow-recovery-decision");

            var execution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                CreatedUtc = DateTimeOffset.UtcNow,
                CurrentStatus = WorkflowStatus.Interrupted,
                RecoveryStatus = WorkflowRecoveryStatus.Recoverable,
                Revision = 0
            };

            await repository.CreateExecutionAsync(execution);

            var decision = new WorkflowRecoveryDecisionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "test",
                DefinitionVersion = "1",
                EvaluatedRevision = 0,
                Decision = RecoveryDecision.Resume,
                RecordedUtc = DateTimeOffset.UtcNow
            };

            await repository.ApplyRecoveryDecisionAsync(
                execution,
                decision);

            var decisions =
                await repository.GetRecoveryDecisionsAsync(workflowId);

            var saved = Assert.Single(decisions);

            Assert.Equal(RecoveryDecision.Resume, saved.Decision);
            Assert.Equal(0, saved.EvaluatedRevision);
            Assert.Equal("test", saved.DefinitionId);
            Assert.Equal("1", saved.DefinitionVersion);

            var updated =
                await repository.GetExecutionAsync(workflowId);

            Assert.NotNull(updated);
            Assert.Equal(1, updated!.Revision);
            Assert.Equal(
                WorkflowRecoveryStatus.Recoverable,
                updated.RecoveryStatus);
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
