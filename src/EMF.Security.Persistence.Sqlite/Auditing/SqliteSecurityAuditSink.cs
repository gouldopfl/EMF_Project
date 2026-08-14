using System.Text.Json;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SqliteSecurityAuditSink :
    ISecurityAuditSink
{
    private readonly string _databasePath;

    public SqliteSecurityAuditSink(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath
            };

        return new SqliteConnection(
            builder.ToString());
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SecurityAuditRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Operation TEXT NOT NULL,
                ResourceType TEXT NOT NULL,
                ResourceId TEXT NOT NULL,
                SubjectId TEXT NOT NULL,
                PolicyDecision TEXT NULL,
                Destination TEXT NULL,
                Outcome TEXT NOT NULL,
                OccurredUtc TEXT NOT NULL,
                FactsJson TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS
                IX_SecurityAuditRecords_Resource
            ON SecurityAuditRecords (
                ResourceType,
                ResourceId,
                OccurredUtc
            );

            CREATE INDEX IF NOT EXISTS
                IX_SecurityAuditRecords_Subject
            ON SecurityAuditRecords (
                SubjectId,
                OccurredUtc
            );
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task WriteAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.ResourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.ResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.SubjectId);
        ArgumentNullException.ThrowIfNull(record.Facts);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO SecurityAuditRecords (
                Operation,
                ResourceType,
                ResourceId,
                SubjectId,
                PolicyDecision,
                Destination,
                Outcome,
                OccurredUtc,
                FactsJson
            )
            VALUES (
                $operation,
                $resourceType,
                $resourceId,
                $subjectId,
                $policyDecision,
                $destination,
                $outcome,
                $occurredUtc,
                $factsJson
            );
            """;

        command.Parameters.AddWithValue(
            "$operation",
            record.Operation);
        command.Parameters.AddWithValue(
            "$resourceType",
            record.ResourceType);
        command.Parameters.AddWithValue(
            "$resourceId",
            record.ResourceId);
        command.Parameters.AddWithValue(
            "$subjectId",
            record.SubjectId);
        command.Parameters.AddWithValue(
            "$policyDecision",
            (object?)record.PolicyDecision?.ToString()
                ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$destination",
            (object?)record.Destination
                ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$outcome",
            record.Outcome.ToString());
        command.Parameters.AddWithValue(
            "$occurredUtc",
            record.OccurredUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$factsJson",
            JsonSerializer.Serialize(record.Facts));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
}
