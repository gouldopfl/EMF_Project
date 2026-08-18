using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowActivityClaimRenewalTests
{
    [Fact]
    public async Task Current_owner_can_renew_claim()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-renew-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("renewal");

            var claimedUtc =
                DateTimeOffset.UtcNow.AddMinutes(-20);

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "claim-1",
                    claimedUtc));

            var renewedUtc =
                DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryRenewActivityClaimAsync(
                    workflowId,
                    "activity",
                    "claim-1",
                    renewedUtc));

            Assert.False(
                await repository.TryReclaimActivityAsync(
                    workflowId,
                    "activity",
                    "claim-2",
                    renewedUtc.AddMinutes(1),
                    renewedUtc.AddMinutes(-15)));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Former_owner_cannot_renew_reclaimed_claim()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-renew-fenced-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("renewal-fenced");

            var now = DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now.AddMinutes(-20)));

            Assert.True(
                await repository.TryReclaimActivityAsync(
                    workflowId,
                    "activity",
                    "new-claim",
                    now,
                    now.AddMinutes(-15)));

            Assert.False(
                await repository.TryRenewActivityClaimAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now.AddMinutes(1)));

            Assert.True(
                await repository.TryRenewActivityClaimAsync(
                    workflowId,
                    "activity",
                    "new-claim",
                    now.AddMinutes(1)));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
