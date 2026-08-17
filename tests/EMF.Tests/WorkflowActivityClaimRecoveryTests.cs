using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowActivityClaimRecoveryTests
{
    [Fact]
    public async Task Abandoned_claim_transfers_ownership()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-recovery-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("recovery");
            var now = DateTimeOffset.UtcNow;

            Assert.True(await repository.TryClaimActivityAsync(
                workflowId, "activity", "old-claim",
                now.AddMinutes(-10)));

            Assert.True(await repository.TryReclaimActivityAsync(
                workflowId, "activity", "new-claim",
                now, now.AddMinutes(-5)));

            await Assert.ThrowsAsync<WorkflowActivityClaimException>(
                () => repository.CompleteActivityClaimAsync(
                    workflowId, "activity", "old-claim", now));

            await repository.CompleteActivityClaimAsync(
                workflowId, "activity", "new-claim", now);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
