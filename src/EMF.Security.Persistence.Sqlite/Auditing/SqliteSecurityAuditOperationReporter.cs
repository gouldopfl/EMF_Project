using EMF.Security.Auditing.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SqliteSecurityAuditOperationReporter
{
    private readonly string _databasePath;

    public SqliteSecurityAuditOperationReporter(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    public async Task<SecurityAuditOperationReport>
        CreateAsync(
            string operation,
            DateTimeOffset? occurredSinceUtc = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

        await using var connection =
            new SqliteConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        using var transaction =
            connection.BeginTransaction();

        var integrity =
            await SqliteSecurityAuditIntegrityVerifier
                .VerifyAsync(
                    connection,
                    transaction,
                    cancellationToken);

        if (!integrity.IsValid)
        {
            throw new InvalidOperationException(
                "Security audit integrity verification failed.");
        }

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText =
            """
            SELECT Outcome, COUNT(*),
                   MIN(OccurredUtc), MAX(OccurredUtc)
            FROM SecurityAuditRecords
            WHERE Operation = $operation
              AND ($occurredSinceUtc IS NULL OR
                   julianday(OccurredUtc) >=
                   julianday($occurredSinceUtc))
            GROUP BY Outcome;
            """;

        command.Parameters.AddWithValue(
            "$operation", operation);

        command.Parameters.AddWithValue(
            "$occurredSinceUtc",
            (object?)occurredSinceUtc?.ToString("O") ??
                DBNull.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var counts =
            new Dictionary<SecurityAuditOutcome, int>();

        var total = 0;
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<SecurityAuditOutcome>(
                    reader.GetString(0),
                    out var outcome))
                throw new InvalidOperationException(
                    "Security audit outcome is invalid.");

            var count = reader.GetInt32(1);
            var groupFirst =
                DateTimeOffset.Parse(reader.GetString(2));
            var groupLast =
                DateTimeOffset.Parse(reader.GetString(3));

            counts.Add(outcome, count);
            total += count;

            if (first is null || groupFirst < first)
                first = groupFirst;

            if (last is null || groupLast > last)
                last = groupLast;
        }

        var report = new SecurityAuditOperationReport
        {
            Operation = operation,
            TotalCount = total,
            OutcomeCounts = counts,
            FirstOccurredUtc = first,
            LastOccurredUtc = last,
            ChainHeadHash = integrity.ChainHeadHash
        };

        transaction.Commit();

        return report;
    }
}
