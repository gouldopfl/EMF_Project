using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed class SqliteWorkflowRepository : IWorkflowRepository
{
    private readonly string _databasePath;

    public SqliteWorkflowRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        };

        return new SqliteConnection(builder.ToString());
    }

    public async Task InitializeAsync(
    CancellationToken cancellationToken = default)
{
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken);

    await using var command = connection.CreateCommand();

    command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS Workflows (
            Id TEXT PRIMARY KEY,
            DefinitionId TEXT NOT NULL,
            DefinitionVersion TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            CurrentStatus TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS WorkflowCheckpoints (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            WorkflowId TEXT NOT NULL,
            Step TEXT NOT NULL,
            ActivityId TEXT NULL,
            Status TEXT NOT NULL,
            RecordedUtc TEXT NOT NULL,
            Message TEXT NULL
        );
        """;

    await command.ExecuteNonQueryAsync(cancellationToken);

    await AddColumnIfMissingAsync(
        connection,
        "Workflows",
        "DefinitionId",
        "TEXT NOT NULL DEFAULT ''",
        cancellationToken);

    await AddColumnIfMissingAsync(
        connection,
        "Workflows",
        "DefinitionVersion",
        "TEXT NOT NULL DEFAULT ''",
        cancellationToken);

    await AddColumnIfMissingAsync(
        connection,
        "Workflows",
        "RecoveryStatus",
        "TEXT NOT NULL DEFAULT 'None'",
        cancellationToken);

    await AddColumnIfMissingAsync(
        connection,
        "WorkflowCheckpoints",
        "ActivityId",
        "TEXT NULL",
        cancellationToken);
}

private static async Task AddColumnIfMissingAsync(
    SqliteConnection connection,
    string tableName,
    string columnName,
    string columnDefinition,
    CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();

    command.CommandText =
        $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";

    try
    {
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    catch (SqliteException ex) when (
        ex.SqliteErrorCode == 1 &&
        ex.Message.Contains(
            "duplicate column name",
            StringComparison.OrdinalIgnoreCase))
    {
    }
}

public async Task CreateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO Workflows
            (
                Id,
                DefinitionId,
                DefinitionVersion,
                CreatedUtc,
                CurrentStatus
            )
            VALUES
            (
                $id,
                $definitionId,
                $definitionVersion,
                $createdUtc,
                $currentStatus
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            execution.WorkflowId.Value);

        command.Parameters.AddWithValue(
            "$definitionId",
            execution.DefinitionId);

        command.Parameters.AddWithValue(
            "$definitionVersion",
            execution.DefinitionVersion);

        command.Parameters.AddWithValue(
            "$createdUtc",
            execution.CreatedUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$currentStatus",
            execution.CurrentStatus.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }




    public async Task UpdateExecutionAsync(
        WorkflowExecutionRecord execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            UPDATE Workflows
            SET CurrentStatus = $currentStatus,
                RecoveryStatus = $recoveryStatus
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$currentStatus",
            execution.CurrentStatus.ToString());

        command.Parameters.AddWithValue(
            "$recoveryStatus",
            execution.RecoveryStatus.ToString());

        command.Parameters.AddWithValue(
            "$id",
            execution.WorkflowId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkflowExecutionRecord?> GetExecutionAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT DefinitionId, DefinitionVersion, CreatedUtc, CurrentStatus, RecoveryStatus
            FROM Workflows
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            workflowId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new WorkflowExecutionRecord
        {
            WorkflowId = workflowId,
            DefinitionId = reader.GetString(0),
            DefinitionVersion = reader.GetString(1),
            CreatedUtc = DateTimeOffset.Parse(reader.GetString(2)),
            CurrentStatus = Enum.Parse<WorkflowStatus>(
                reader.GetString(3)),
        RecoveryStatus = Enum.Parse<WorkflowRecoveryStatus>(
            reader.GetString(4))
        };
    }

    public async Task AddCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO WorkflowCheckpoints
            (
                WorkflowId,
                Step,
                ActivityId,
                Status,
                RecordedUtc,
                Message
            )
            VALUES
            (
                $workflowId,
                $step,
                $activityId,
                $status,
                $recordedUtc,
                $message
            );
            """;

        command.Parameters.AddWithValue(
            "$workflowId",
            checkpoint.WorkflowId.Value);

        command.Parameters.AddWithValue(
            "$step",
            checkpoint.Step);

        command.Parameters.AddWithValue(
            "$activityId",
            (object?)checkpoint.ActivityId ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$status",
            checkpoint.Status.ToString());

        command.Parameters.AddWithValue(
            "$recordedUtc",
            checkpoint.RecordedUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$message",
            (object?)checkpoint.Message ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Step, ActivityId, Status, RecordedUtc, Message
            FROM WorkflowCheckpoints
            WHERE WorkflowId = $workflowId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$workflowId",
            workflowId.Value);

        var checkpoints = new List<WorkflowCheckpoint>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            checkpoints.Add(
                new WorkflowCheckpoint
                {
                    WorkflowId = workflowId,
                    Step = reader.GetString(0),
                    ActivityId = reader.IsDBNull(1)
                        ? null
                        : reader.GetString(1),
                    Status = Enum.Parse<WorkflowStatus>(
                        reader.GetString(2)),
                    RecordedUtc =
                        DateTimeOffset.Parse(reader.GetString(3)),
                    Message = reader.IsDBNull(4)
                        ? null
                        : reader.GetString(4)
                });
        }

        return checkpoints;
    }
}
