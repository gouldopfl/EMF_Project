using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class WorkflowPersistenceTests
{
    [Fact]
    public async Task SqliteWorkflowRepository_InitializeAsync_CreatesSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            Assert.True(File.Exists(databasePath));

            await using var connection =
                new SqliteConnection($"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Workflows', 'WorkflowCheckpoints');
                """;

            var tables = new List<string>();

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Contains("Workflows", tables);
            Assert.Contains("WorkflowCheckpoints", tables);
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
    public async Task SqliteWorkflowRepository_CheckpointRoundTrip()
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
                new WorkflowId("workflow-001");

            var checkpoint = new WorkflowCheckpoint
            {
                WorkflowId = workflowId,
                Step = "Fingerprinting Complete",
                Status = WorkflowStatus.Completed,
                RecordedUtc = DateTimeOffset.UtcNow,
                Message = "Processed evidence package"
            };

            await repository.AddCheckpointAsync(checkpoint);

            var results =
                await repository.GetCheckpointsAsync(workflowId);

            var result = Assert.Single(results);

            Assert.Equal(
                workflowId,
                result.WorkflowId);

            Assert.Equal(
                "Fingerprinting Complete",
                result.Step);

            Assert.Equal(
                WorkflowStatus.Completed,
                result.Status);

            Assert.Equal(
                "Processed evidence package",
                result.Message);
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
