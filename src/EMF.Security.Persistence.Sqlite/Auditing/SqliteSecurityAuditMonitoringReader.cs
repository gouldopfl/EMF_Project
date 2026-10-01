using System.Globalization;
using EMF.Security.Monitoring;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SqliteSecurityAuditMonitoringReader : ISecurityAuditMonitoringReader
{
    private readonly string _databasePath;
    private readonly string _sourceId;
    private readonly Func<Task>? _afterVerification;

    public SqliteSecurityAuditMonitoringReader(string databasePath, string sourceId)
        : this(databasePath, sourceId, null) { }

    // Deterministic concurrency-test seam, unavailable to deployment callers.
    internal SqliteSecurityAuditMonitoringReader(string databasePath, string sourceId, Func<Task>? afterVerification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!SecurityMonitoringValidation.IsIdentifier(sourceId)) throw new ArgumentException("Invalid audit source identity.", nameof(sourceId));
        _databasePath = databasePath;
        _sourceId = sourceId;
        _afterVerification = afterVerification;
    }

    public async Task<SecurityAuditMonitoringSnapshot> ReadAsync(IReadOnlyList<SecurityTelemetryExpectation> expectations,
        DateTimeOffset evaluationUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        foreach (var expectation in expectations) SecurityMonitoringValidation.Validate(expectation);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: true);
            var result = await SqliteSecurityAuditIntegrityVerifier.VerifyAsync(connection, transaction, cancellationToken);
            var diagnostics = new SecurityAuditIntegrityDiagnostics
            {
                IsValid = result.IsValid,
                FailureCategory = result.FailureCategory,
                FirstInvalidRecordId = result.InvalidRecordId,
                ProtectedPrefixCount = result.ProtectedRecordCount,
                LegacyPrefixCount = result.LegacyRecordCount
            };
            if (!result.IsValid) return new() { SourceId = _sourceId, Integrity = diagnostics };
            if (_afterVerification is not null) await _afterVerification();
            var observations = new Dictionary<long, SecurityTelemetryObservation>();
            foreach (var expectation in expectations.Where(e => e.SourceId == _sourceId))
            {
                var baseline = expectation.MaintenanceIntervals
                    .Where(i => i.EndedUtc <= evaluationUtc).Select(i => i.EndedUtc)
                    .Append(expectation.EffectiveFromUtc).Max();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT Id, Operation, ResourceType, ResourceId, OccurredUtc
                    FROM SecurityAuditRecords
                    WHERE IntegrityVersion=1 AND Operation=$operation
                      AND ($resourceType IS NULL OR (ResourceType=$resourceType AND ResourceId=$resourceId))
                    """;
                command.Parameters.AddWithValue("$operation", expectation.Operation);
                command.Parameters.AddWithValue("$resourceType", (object?)expectation.ResourceType ?? DBNull.Value);
                command.Parameters.AddWithValue("$resourceId", (object?)expectation.ResourceId ?? DBNull.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                SecurityTelemetryObservation? latest = null;
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!DateTimeOffset.TryParseExact(reader.GetString(4), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred))
                        throw new SecurityTelemetryEvidenceException();
                    if (occurred > evaluationUtc || occurred < expectation.EffectiveFromUtc) continue;
                    // Return only the latest qualifying observation for the current explicit active period.
                    if (occurred < baseline) continue;
                    if (latest is null || occurred > latest.OccurredUtc)
                        latest = new()
                        {
                            SourceId = _sourceId,
                            RecordId = reader.GetInt64(0),
                            Operation = reader.GetString(1),
                            ResourceType = reader.GetString(2),
                            ResourceId = reader.GetString(3),
                            OccurredUtc = occurred.ToUniversalTime()
                        };
                }
                if (latest is not null) observations[latest.RecordId] = latest;
            }
            transaction.Commit();
            return new() { SourceId = _sourceId, Integrity = diagnostics, Observations = observations.Values.ToArray() };
        }
        catch (SqliteException ex)
        {
            throw new SecurityAuditMonitoringUnavailableException(ex);
        }
    }
}
