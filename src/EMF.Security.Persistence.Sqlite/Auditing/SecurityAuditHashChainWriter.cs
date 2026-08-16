using System.Text.Json;
using EMF.Security.Auditing.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

internal static class SecurityAuditHashChainWriter
{
    public static async Task WriteAsync(
        SqliteConnection connection,
        SecurityAuditRecord record,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            connection.BeginTransaction(
                deferred: false);

        var previousRecordHash =
            await GetPreviousRecordHashAsync(
                connection,
                transaction,
                cancellationToken);

        var integrityVersion =
            SecurityAuditRecordHasher.CurrentVersion;

        var policyDecision =
            record.PolicyDecision?.ToString();

        var outcome =
            record.Outcome.ToString();

        var occurredUtc =
            record.OccurredUtc.ToString("O");

        var factsJson =
            JsonSerializer.Serialize(record.Facts);

        var recordHash =
            SecurityAuditRecordHasher.ComputeHash(
                integrityVersion,
                previousRecordHash,
                record.Operation,
                record.ResourceType,
                record.ResourceId,
                record.SubjectId,
                policyDecision,
                record.Destination,
                outcome,
                occurredUtc,
                factsJson);

        await InsertAsync(
            connection,
            transaction,
            record,
            integrityVersion,
            previousRecordHash,
            recordHash,
            policyDecision,
            outcome,
            occurredUtc,
            factsJson,
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task<string?>
        GetPreviousRecordHashAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                IntegrityVersion,
                RecordHash,
                EXISTS (
                    SELECT 1
                    FROM SecurityAuditRecords
                    WHERE IntegrityVersion = $currentVersion
                )
            FROM SecurityAuditRecords
            ORDER BY Id DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$currentVersion",
            SecurityAuditRecordHasher.CurrentVersion);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        var integrityVersion =
            reader.GetInt32(0);

        var recordHash =
            reader.IsDBNull(1)
                ? null
                : reader.GetString(1);

        var hasProtectedRecords =
            reader.GetInt32(2) != 0;

        if (integrityVersion == 0 &&
            !hasProtectedRecords)
        {
            return null;
        }

        if (integrityVersion !=
                SecurityAuditRecordHasher.CurrentVersion ||
            string.IsNullOrWhiteSpace(recordHash))
        {
            throw new InvalidOperationException(
                "Security audit hash chain is not appendable.");
        }

        return recordHash;
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SecurityAuditRecord record,
        int integrityVersion,
        string? previousRecordHash,
        string recordHash,
        string? policyDecision,
        string outcome,
        string occurredUtc,
        string factsJson,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;
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
                FactsJson,
                IntegrityVersion,
                PreviousRecordHash,
                RecordHash
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
                $factsJson,
                $integrityVersion,
                $previousRecordHash,
                $recordHash
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
            (object?)policyDecision ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$destination",
            (object?)record.Destination ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$outcome",
            outcome);
        command.Parameters.AddWithValue(
            "$occurredUtc",
            occurredUtc);
        command.Parameters.AddWithValue(
            "$factsJson",
            factsJson);
        command.Parameters.AddWithValue(
            "$integrityVersion",
            integrityVersion);
        command.Parameters.AddWithValue(
            "$previousRecordHash",
            (object?)previousRecordHash ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$recordHash",
            recordHash);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
}
