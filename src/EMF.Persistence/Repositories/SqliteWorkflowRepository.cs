using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteWorkflowRepository : IWorkflowRepository
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

        
        CREATE TABLE IF NOT EXISTS WorkflowStatusTransitions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            WorkflowId TEXT NOT NULL,
            FromStatus TEXT NOT NULL,
            ToStatus TEXT NOT NULL,
            RecordedUtc TEXT NOT NULL,
            Message TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS WorkflowActivityClaims (
            WorkflowId TEXT NOT NULL,
            ActivityId TEXT NOT NULL,
            ClaimId TEXT NOT NULL UNIQUE,
            Status TEXT NOT NULL,
            ClaimedUtc TEXT NOT NULL,
            CompletedUtc TEXT NULL,
            PRIMARY KEY (WorkflowId, ActivityId)
        );

        CREATE TABLE IF NOT EXISTS WorkflowOperations (
            WorkflowId TEXT NOT NULL,
            ActivityId TEXT NOT NULL,
            OperationId TEXT NOT NULL,
            OperationType TEXT NOT NULL,
            Status TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            CompletedUtc TEXT NULL,
            PRIMARY KEY (WorkflowId, ActivityId, OperationId)
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
        "Workflows",
        "Revision",
        "INTEGER NOT NULL DEFAULT 0",
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
                CurrentStatus,
                RecoveryStatus
            )
            VALUES
            (
                $id,
                $definitionId,
                $definitionVersion,
                $createdUtc,
                $currentStatus,
                $recoveryStatus
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

        command.Parameters.AddWithValue(
            "$recoveryStatus",
            execution.RecoveryStatus.ToString());

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
                RecoveryStatus = $recoveryStatus,
                Revision = Revision + 1
            WHERE Id = $id
              AND Revision = $expectedRevision;
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

        command.Parameters.AddWithValue(
            "$expectedRevision",
            execution.Revision);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new WorkflowConcurrencyException(
                execution.WorkflowId,
                execution.Revision);
        }
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
            SELECT DefinitionId, DefinitionVersion, CreatedUtc,
                   CurrentStatus, RecoveryStatus, Revision
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
            CurrentStatus =
                Enum.Parse<WorkflowStatus>(
                    reader.GetString(3)),
            RecoveryStatus =
                Enum.Parse<WorkflowRecoveryStatus>(
                    reader.GetString(4)),
            Revision = reader.GetInt64(5)
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

    public async Task AddStatusTransitionAsync(
        WorkflowStatusTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO WorkflowStatusTransitions
            (
                WorkflowId,
                FromStatus,
                ToStatus,
                RecordedUtc,
                Message
            )
            VALUES
            (
                $workflowId,
                $fromStatus,
                $toStatus,
                $recordedUtc,
                $message
            );
            """;

        command.Parameters.AddWithValue(
            "$workflowId",
            transition.WorkflowId.Value);

        command.Parameters.AddWithValue(
            "$fromStatus",
            transition.FromStatus.ToString());

        command.Parameters.AddWithValue(
            "$toStatus",
            transition.ToStatus.ToString());

        command.Parameters.AddWithValue(
            "$recordedUtc",
            transition.RecordedUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$message",
            (object?)transition.Message ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    public async Task<IReadOnlyList<WorkflowStatusTransition>> GetStatusTransitionsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT FromStatus, ToStatus, RecordedUtc, Message
            FROM WorkflowStatusTransitions
            WHERE WorkflowId = $workflowId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$workflowId",
            workflowId.Value);

        var transitions = new List<WorkflowStatusTransition>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(
                new WorkflowStatusTransition
                {
                    WorkflowId = workflowId,
                    FromStatus = Enum.Parse<WorkflowStatus>(
                        reader.GetString(0)),
                    ToStatus = Enum.Parse<WorkflowStatus>(
                        reader.GetString(1)),
                    RecordedUtc =
                        DateTimeOffset.Parse(reader.GetString(2)),
                    Message = reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3)
                });
        }

        return transitions;
    }


    public async Task ApplyStatusTransitionAsync(
        WorkflowExecutionRecord execution,
        WorkflowStatusTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(transition);

        if (execution.WorkflowId != transition.WorkflowId)
        {
            throw new ArgumentException(
                "Execution and transition must reference the same workflow.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;

                updateCommand.CommandText =
                    """
                    UPDATE Workflows
                    SET CurrentStatus = $currentStatus,
                        RecoveryStatus = $recoveryStatus,
                        Revision = Revision + 1
                    WHERE Id = $id
                      AND Revision = $expectedRevision;
                    """;

                updateCommand.Parameters.AddWithValue(
                    "$currentStatus",
                    execution.CurrentStatus.ToString());

                updateCommand.Parameters.AddWithValue(
                    "$recoveryStatus",
                    execution.RecoveryStatus.ToString());

                updateCommand.Parameters.AddWithValue(
                    "$id",
                    execution.WorkflowId.Value);

                updateCommand.Parameters.AddWithValue(
                    "$expectedRevision",
                    execution.Revision);

                var affectedRows =
                    await updateCommand.ExecuteNonQueryAsync(
                        cancellationToken);

                if (affectedRows != 1)
                {
                    throw new WorkflowConcurrencyException(
                        execution.WorkflowId,
                        execution.Revision);
                }
            }

            await using (var transitionCommand = connection.CreateCommand())
            {
                transitionCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;

                transitionCommand.CommandText =
                    """
                    INSERT INTO WorkflowStatusTransitions
                    (
                        WorkflowId,
                        FromStatus,
                        ToStatus,
                        RecordedUtc,
                        Message
                    )
                    VALUES
                    (
                        $workflowId,
                        $fromStatus,
                        $toStatus,
                        $recordedUtc,
                        $message
                    );
                    """;

                transitionCommand.Parameters.AddWithValue(
                    "$workflowId",
                    transition.WorkflowId.Value);

                transitionCommand.Parameters.AddWithValue(
                    "$fromStatus",
                    transition.FromStatus.ToString());

                transitionCommand.Parameters.AddWithValue(
                    "$toStatus",
                    transition.ToStatus.ToString());

                transitionCommand.Parameters.AddWithValue(
                    "$recordedUtc",
                    transition.RecordedUtc.ToString("O"));

                transitionCommand.Parameters.AddWithValue(
                    "$message",
                    (object?)transition.Message ?? DBNull.Value);

                await transitionCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

}
