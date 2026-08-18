using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteWorkflowRepository
{
    public async Task<WorkflowOperationRecord?> GetOperationAsync(
        WorkflowId workflowId,
        string activityId,
        OperationId operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT WorkflowId,
                   ActivityId,
                   OperationId,
                   OperationType,
                   Status,
                   CreatedUtc,
                   CompletedUtc
            FROM WorkflowOperations
            WHERE WorkflowId = $workflowId
              AND ActivityId = $activityId
              AND OperationId = $operationId;
            """;

        command.Parameters.AddWithValue(
            "$workflowId",
            workflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId",
            activityId);
        command.Parameters.AddWithValue(
            "$operationId",
            operationId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new WorkflowOperationRecord
        {
            WorkflowId = new WorkflowId(reader.GetString(0)),
            ActivityId = reader.GetString(1),
            OperationId = new OperationId(reader.GetString(2)),
            OperationType = reader.GetString(3),
            Status = reader.GetString(4),
            CreatedUtc = DateTimeOffset.Parse(reader.GetString(5)),
            CompletedUtc = reader.IsDBNull(6)
                ? null
                : DateTimeOffset.Parse(reader.GetString(6))
        };
    }

    public async Task<IReadOnlyList<WorkflowOperationRecord>> GetOperationsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT WorkflowId, ActivityId, OperationId, OperationType, " +
            "Status, CreatedUtc, CompletedUtc " +
            "FROM WorkflowOperations " +
            "WHERE WorkflowId = $workflowId " +
            "ORDER BY CreatedUtc, ActivityId, OperationId;";

        command.Parameters.AddWithValue(
            "$workflowId",
            workflowId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var operations = new List<WorkflowOperationRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(
                new WorkflowOperationRecord
                {
                    WorkflowId = new WorkflowId(reader.GetString(0)),
                    ActivityId = reader.GetString(1),
                    OperationId = new OperationId(reader.GetString(2)),
                    OperationType = reader.GetString(3),
                    Status = reader.GetString(4),
                    CreatedUtc = DateTimeOffset.Parse(reader.GetString(5)),
                    CompletedUtc = reader.IsDBNull(6)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(6))
                });
        }

        return operations;
    }

    public async Task<bool> TryCreateOperationAsync(
        WorkflowOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.ActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.OperationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Status);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            "INSERT INTO WorkflowOperations " +
            "(WorkflowId, ActivityId, OperationId, OperationType, " +
            "Status, CreatedUtc, CompletedUtc) " +
            "VALUES ($workflowId, $activityId, $operationId, " +
            "$operationType, $status, $createdUtc, $completedUtc) " +
            "ON CONFLICT (WorkflowId, ActivityId, OperationId) DO NOTHING;";

        command.Parameters.AddWithValue(
            "$workflowId", operation.WorkflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId", operation.ActivityId);
        command.Parameters.AddWithValue(
            "$operationId", operation.OperationId.Value);
        command.Parameters.AddWithValue(
            "$operationType", operation.OperationType);
        command.Parameters.AddWithValue(
            "$status", operation.Status);
        command.Parameters.AddWithValue(
            "$createdUtc", operation.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$completedUtc",
            (object?)operation.CompletedUtc?.ToString("O")
                ?? DBNull.Value);

        return await command.ExecuteNonQueryAsync(
            cancellationToken) == 1;
    }


    public async Task UpdateOperationAsync(
        WorkflowOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.ActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.OperationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Status);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            "UPDATE WorkflowOperations " +
            "SET OperationType = $operationType, " +
            "Status = $status, " +
            "CreatedUtc = $createdUtc, " +
            "CompletedUtc = $completedUtc " +
            "WHERE WorkflowId = $workflowId " +
            "AND ActivityId = $activityId " +
            "AND OperationId = $operationId;";

        command.Parameters.AddWithValue(
            "$workflowId", operation.WorkflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId", operation.ActivityId);
        command.Parameters.AddWithValue(
            "$operationId", operation.OperationId.Value);
        command.Parameters.AddWithValue(
            "$operationType", operation.OperationType);
        command.Parameters.AddWithValue(
            "$status", operation.Status);
        command.Parameters.AddWithValue(
            "$createdUtc", operation.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$completedUtc",
            (object?)operation.CompletedUtc?.ToString("O")
                ?? DBNull.Value);

        var affected = await command.ExecuteNonQueryAsync(
            cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Workflow operation '{operation.OperationId}' was not found.");
        }
    }
}
