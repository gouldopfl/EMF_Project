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

    [Fact]
    public async Task Concurrent_reclaim_attempts_allow_only_one_new_owner()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-concurrent-recovery-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("concurrent-recovery");

            var now =
                DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now.AddMinutes(-10)));

            var first =
                repository.TryReclaimActivityAsync(
                    workflowId,
                    "activity",
                    "new-claim-1",
                    now,
                    now.AddMinutes(-5));

            var second =
                repository.TryReclaimActivityAsync(
                    workflowId,
                    "activity",
                    "new-claim-2",
                    now,
                    now.AddMinutes(-5));

            var results =
                await Task.WhenAll(first, second);

            Assert.Equal(
                1,
                results.Count(result => result));

            Assert.Equal(
                1,
                results.Count(result => !result));

            var winningClaimId =
                results[0]
                    ? "new-claim-1"
                    : "new-claim-2";

            var losingClaimId =
                results[0]
                    ? "new-claim-2"
                    : "new-claim-1";

            await Assert.ThrowsAsync<WorkflowActivityClaimException>(
                () => repository.CompleteActivityClaimAsync(
                    workflowId,
                    "activity",
                    losingClaimId,
                    now));

            await repository.CompleteActivityClaimAsync(
                workflowId,
                "activity",
                winningClaimId,
                now);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Fresh_claim_cannot_be_reclaimed()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-fresh-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("fresh");
            var now = DateTimeOffset.UtcNow;

            Assert.True(await repository.TryClaimActivityAsync(
                workflowId, "activity", "current-claim", now));

            Assert.False(await repository.TryReclaimActivityAsync(
                workflowId, "activity", "new-claim",
                now, now.AddMinutes(-5)));

            await repository.CompleteActivityClaimAsync(
                workflowId, "activity", "current-claim", now);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Completed_claim_cannot_be_reclaimed()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-completed-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("completed");
            var now = DateTimeOffset.UtcNow;

            await repository.TryClaimActivityAsync(
                workflowId, "activity", "completed-claim",
                now.AddHours(-1));

            await repository.CompleteActivityClaimAsync(
                workflowId, "activity", "completed-claim", now);

            Assert.False(await repository.TryReclaimActivityAsync(
                workflowId, "activity", "new-claim",
                now, now.AddMinutes(-5)));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
