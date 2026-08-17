using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowActivityClaimPersistenceTests
{
    [Fact]
    public async Task Claim_is_exclusive_and_owner_can_complete_it()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("workflow-claim");

            var first = await repository.TryClaimActivityAsync(
                workflowId,
                "inventory",
                "claim-1",
                DateTimeOffset.UtcNow);

            var second = await repository.TryClaimActivityAsync(
                workflowId,
                "inventory",
                "claim-2",
                DateTimeOffset.UtcNow);

            Assert.True(first);
            Assert.False(second);

            await repository.CompleteActivityClaimAsync(
                workflowId,
                "inventory",
                "claim-1",
                DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<WorkflowActivityClaimException>(
                () => repository.ReleaseActivityClaimAsync(
                    workflowId,
                    "inventory",
                    "claim-1"));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Released_failed_claim_can_be_retried()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("workflow-claim-retry");

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "inventory",
                    "claim-1",
                    DateTimeOffset.UtcNow));

            await repository.ReleaseActivityClaimAsync(
                workflowId,
                "inventory",
                "claim-1");

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "inventory",
                    "claim-2",
                    DateTimeOffset.UtcNow));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");
    }

    private static void DeleteDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
