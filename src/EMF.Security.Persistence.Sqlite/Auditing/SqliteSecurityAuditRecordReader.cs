using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SecurityAuditRecordData
{
    public required string ResourceId { get; init; }

    public required string SubjectId { get; init; }

    public string? Destination { get; init; }

    public required string Outcome { get; init; }

    public required string FactsJson { get; init; }
}

public sealed class SqliteSecurityAuditRecordReader
{
    private readonly string _databasePath;

    public SqliteSecurityAuditRecordReader(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    public async Task<IReadOnlyList<SecurityAuditRecordData>>
        FindByFactAsync(
            string operation,
            string resourceType,
            string resourceId,
            string factName,
            string factValue,
            int maxResults = 2,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(factName);
        ArgumentException.ThrowIfNullOrWhiteSpace(factValue);

        if (maxResults <= 0 || maxResults > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults));
        }

        try
        {
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

            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT ResourceId, SubjectId, Destination,
                       Outcome, FactsJson
                FROM SecurityAuditRecords
                WHERE Operation = $operation
                  AND ResourceType = $resourceType
                  AND ResourceId = $resourceId
                  AND json_extract(FactsJson, $jsonPath) = $factValue
                ORDER BY Id DESC
                LIMIT $maxResults;
                """;

            command.Parameters.AddWithValue(
                "$operation", operation);
            command.Parameters.AddWithValue(
                "$resourceType", resourceType);
            command.Parameters.AddWithValue(
                "$resourceId", resourceId);
            command.Parameters.AddWithValue(
                "$jsonPath", $"$.{factName}");
            command.Parameters.AddWithValue(
                "$factValue", factValue);
            command.Parameters.AddWithValue(
                "$maxResults", maxResults);

            var results =
                new List<SecurityAuditRecordData>();

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(
                    new SecurityAuditRecordData
                    {
                        ResourceId = reader.GetString(0),
                        SubjectId = reader.GetString(1),
                        Destination =
                            reader.IsDBNull(2)
                                ? null
                                : reader.GetString(2),
                        Outcome = reader.GetString(3),
                        FactsJson = reader.GetString(4)
                    });
            }

            transaction.Commit();

            return results;
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException(
                "Security audit record lookup failed.",
                exception);
        }
    }
}
