using EMF.Security.Monitoring;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class
    SqliteSecurityAuditIntegrityVerifier
{
    private readonly string _databasePath;

    public SqliteSecurityAuditIntegrityVerifier(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    public async Task<
        SecurityAuditIntegrityVerificationResult>
        VerifyAsync(
            CancellationToken cancellationToken = default)
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

        await using var connection =
            new SqliteConnection(connectionString);

        await connection.OpenAsync(
            cancellationToken);

        return await VerifyAsync(
            connection,
            null,
            cancellationToken);
    }

    internal static async Task<
        SecurityAuditIntegrityVerificationResult>
        VerifyAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = $"SELECT {CanonicalSecurityAuditWriter.Projection} FROM SecurityAuditRecords ORDER BY Id;";

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var legacyRecordCount = 0;
        var protectedRecordCount = 0;
        string? expectedPreviousHash = null;
        long? lastProtectedRecordId = null;
        var protectedRecordsStarted = false;
        var v2Started = false;
        var eventIds = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var recordId =
                reader.GetInt64(0);

            var integrityVersion =
                reader.GetInt32(10);

            var previousRecordHash =
                GetNullableString(reader, 11);

            var recordHash =
                GetNullableString(reader, 12);

            if (integrityVersion == 0)
            {
                if (protectedRecordsStarted ||
                    previousRecordHash is not null ||
                    recordHash is not null || Enumerable.Range(13, 5).Any(i => !reader.IsDBNull(i)))
                {
                    return Invalid(
                        protectedRecordCount,
                        legacyRecordCount,
                        recordId,
                        SecurityAuditIntegrityFailureCategory.InvalidLegacyPlacement,
                        "Legacy record appears inside the protected chain.");
                }

                legacyRecordCount++;
                continue;
            }

            if (integrityVersion is not (1 or 2) || integrityVersion == 1 && v2Started)
            {
                return Invalid(
                    protectedRecordCount,
                    legacyRecordCount,
                    recordId,
                    SecurityAuditIntegrityFailureCategory.UnsupportedIntegrityVersion,
                    "Unsupported audit integrity version.");
            }

            protectedRecordsStarted = true;

            if (recordHash is null)
            {
                return Invalid(protectedRecordCount, legacyRecordCount, recordId,
                    SecurityAuditIntegrityFailureCategory.MissingRecordHash,
                    "Protected audit record hash is missing.");
            }

            if (!string.Equals(
                    previousRecordHash,
                    expectedPreviousHash,
                    StringComparison.Ordinal))
            {
                return Invalid(
                    protectedRecordCount,
                    legacyRecordCount,
                    recordId,
                    SecurityAuditIntegrityFailureCategory.PreviousHashMismatch,
                    "Previous audit record hash does not match.");
            }

            string computedHash;
            try
            {
                if (integrityVersion == 2)
                {
                    v2Started = true;
                    var record = CanonicalSecurityAuditWriter.Decode(reader);
                    if (!eventIds.Add(record.AuditEventId!.Value.Value)) throw new ArgumentException("Duplicate audit identity.");
                    computedHash = EMF.Security.Auditing.SecurityAuditCanonicalEvent.Hash(previousRecordHash,
                        EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(record));
                }
                else
                {
                    if (Enumerable.Range(13, 5).Any(i => !reader.IsDBNull(i))) throw new ArgumentException("Historical identities were changed.");
                    computedHash =
                SecurityAuditRecordHasher.ComputeHash(
                    integrityVersion,
                    previousRecordHash,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    GetNullableString(reader, 5),
                    GetNullableString(reader, 6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9));

                }
            }
            catch (Exception error) when (error is ArgumentException or FormatException or System.Text.Json.JsonException or InvalidOperationException)
            {
                return Invalid(protectedRecordCount, legacyRecordCount, recordId,
                    SecurityAuditIntegrityFailureCategory.RecordHashMismatch, "Malformed canonical audit record.");
            }

            if (!string.Equals(
                    recordHash,
                    computedHash,
                    StringComparison.Ordinal))
            {
                return Invalid(
                    protectedRecordCount,
                    legacyRecordCount,
                    recordId,
                    SecurityAuditIntegrityFailureCategory.RecordHashMismatch,
                    "Audit record hash does not match its content.");
            }

            protectedRecordCount++;
            expectedPreviousHash = recordHash;
            lastProtectedRecordId = recordId;
        }

        return new SecurityAuditIntegrityVerificationResult
        {
            IsValid = true,
            ProtectedRecordCount =
                protectedRecordCount,
            LegacyRecordCount =
                legacyRecordCount,
            LastProtectedRecordId =
                lastProtectedRecordId,
            ChainHeadHash =
                expectedPreviousHash
        };
    }

    private static string? GetNullableString(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static
        SecurityAuditIntegrityVerificationResult
        Invalid(
            int protectedRecordCount,
            int legacyRecordCount,
            long recordId,
            SecurityAuditIntegrityFailureCategory failureCategory,
            string failureReason)
    {
        return new SecurityAuditIntegrityVerificationResult
        {
            IsValid = false,
            ProtectedRecordCount =
                protectedRecordCount,
            LegacyRecordCount =
                legacyRecordCount,
            InvalidRecordId = recordId,
            FailureCategory = failureCategory,
            FailureReason = failureReason
        };
    }
}
