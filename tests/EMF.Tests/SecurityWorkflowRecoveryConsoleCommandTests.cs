using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class SecurityWorkflowRecoveryConsoleCommandTests
{
    [Fact]
    public async Task WorkflowRecover_reclaims_abandoned_claim()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-console-recovery-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("console-recovery");

            var now = DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now.AddMinutes(-20)));

            var exitCode =
                await SecurityConsoleCommand.RunAsync(
                [
                    "workflow",
                    "recover",
                    path,
                    workflowId.Value,
                    "activity",
                    "new-claim"
                ]);

            Assert.Equal(0, exitCode);

            await Assert.ThrowsAsync<
                WorkflowActivityClaimException>(
                () => repository.CompleteActivityClaimAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now));

            await repository.CompleteActivityClaimAsync(
                workflowId,
                "activity",
                "new-claim",
                now);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task WorkflowRecover_returns_one_for_fresh_claim()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-console-fresh-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("console-fresh");

            var now = DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "current-claim",
                    now));

            var exitCode =
                await SecurityConsoleCommand.RunAsync(
                [
                    "workflow",
                    "recover",
                    path,
                    workflowId.Value,
                    "activity",
                    "new-claim"
                ]);

            Assert.Equal(1, exitCode);

            await repository.CompleteActivityClaimAsync(
                workflowId,
                "activity",
                "current-claim",
                now);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }


    [Fact]
    public async Task WorkflowRecover_denies_regulated_classification()
    {
        var path = Path.GetTempFileName();
        var previous =
            Environment.GetEnvironmentVariable(
                "EMF_PROTECTION_CLASSIFICATION");

        try
        {
            Environment.SetEnvironmentVariable(
                "EMF_PROTECTION_CLASSIFICATION",
                "regulated");

            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("regulated-recovery");

            var now = DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now.AddMinutes(-20)));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => SecurityWorkflowRecoveryConsoleCommand.RunAsync(
                    [
                        path,
                        workflowId.Value,
                        "activity",
                        "new-claim"
                    ]));

            await repository.CompleteActivityClaimAsync(
                workflowId,
                "activity",
                "old-claim",
                now);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "EMF_PROTECTION_CLASSIFICATION",
                previous);

            File.Delete(path);
        }
    }

}
