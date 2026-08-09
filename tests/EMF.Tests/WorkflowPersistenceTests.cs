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

public sealed class WorkflowExecutionPersistenceTests
{
    [Fact]
    public async Task SqliteWorkflowRepository_ExecutionRoundTrip()
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
                new WorkflowId("workflow-execution-001");

            var createdUtc =
                DateTimeOffset.UtcNow;

            var execution = new WorkflowExecutionRecord
            {
                WorkflowId = workflowId,
                DefinitionId = "evidence-processing",
                DefinitionVersion = "1",
                CreatedUtc = createdUtc,
                CurrentStatus = WorkflowStatus.Running,
                RecoveryStatus = WorkflowRecoveryStatus.Recoverable
            };

            await repository.CreateExecutionAsync(execution);

            var stored =
                await repository.GetExecutionAsync(workflowId);

            Assert.NotNull(stored);
            Assert.Equal(workflowId, stored!.WorkflowId);
            Assert.Equal("evidence-processing", stored.DefinitionId);
            Assert.Equal("1", stored.DefinitionVersion);
            Assert.Equal(createdUtc, stored.CreatedUtc);
            Assert.Equal(WorkflowStatus.Running, stored.CurrentStatus);
            Assert.Equal(
                WorkflowRecoveryStatus.Recoverable,
                stored.RecoveryStatus);
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

public sealed class WorkflowSchemaMigrationTests
{
    [Fact]
    public async Task SqliteWorkflowRepository_InitializeAsync_UpgradesLegacyWorkflowSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection =
                new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TABLE Workflows (
                        Id TEXT PRIMARY KEY,
                        CreatedUtc TEXT NOT NULL,
                        CurrentStatus TEXT NOT NULL
                    );

                    CREATE TABLE WorkflowCheckpoints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        WorkflowId TEXT NOT NULL,
                        Step TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        RecordedUtc TEXT NOT NULL,
                        Message TEXT NULL
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            await using var verificationConnection =
                new SqliteConnection($"Data Source={databasePath}");

            await verificationConnection.OpenAsync();

            await using var verificationCommand =
                verificationConnection.CreateCommand();

            verificationCommand.CommandText =
                """
                PRAGMA table_info(Workflows);
                """;

            var columns = new List<string>();

            await using var reader =
                await verificationCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            Assert.Contains("DefinitionId", columns);
            Assert.Contains("DefinitionVersion", columns);
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
