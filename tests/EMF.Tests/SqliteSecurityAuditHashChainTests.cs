using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SqliteSecurityAuditHashChainTests
{
    [Fact]
    public async Task WriteAsync_links_consecutive_records()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "audit-chain.db");

            var sink =
                new SqliteSecurityAuditSink(
                    databasePath);

            await sink.InitializeAsync();

            await sink.WriteAsync(
                CreateRecord(
                    "artifact-001",
                    SecurityAuditOutcome.Succeeded));

            await sink.WriteAsync(
                CreateRecord(
                    "artifact-002",
                    SecurityAuditOutcome.Denied));

            await using var connection =
                new SqliteConnection(
                    $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    IntegrityVersion,
                    PreviousRecordHash,
                    RecordHash
                FROM SecurityAuditRecords
                ORDER BY Id;
                """;

            await using var reader =
                await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.True(reader.IsDBNull(1));

            var firstHash =
                reader.GetString(2);

            Assert.Equal(64, firstHash.Length);

            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(
                firstHash,
                reader.GetString(1));

            var secondHash =
                reader.GetString(2);

            Assert.Equal(64, secondHash.Length);
            Assert.NotEqual(firstHash, secondHash);
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAsync_serializes_concurrent_writers()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(
                    root,
                    "concurrent-audit-chain.db");

            var sink =
                new SqliteSecurityAuditSink(
                    databasePath);

            await sink.InitializeAsync();

            const int recordCount = 32;

            var writes =
                Enumerable.Range(1, recordCount)
                    .Select(
                        index =>
                            sink.WriteAsync(
                                CreateRecord(
                                    $"artifact-{index:D3}",
                                    SecurityAuditOutcome
                                        .Succeeded)))
                    .ToArray();

            await Task.WhenAll(writes);

            var verifier =
                new SqliteSecurityAuditIntegrityVerifier(
                    databasePath);

            var result =
                await verifier.VerifyAsync();

            Assert.True(result.IsValid);
            Assert.Equal(
                recordCount,
                result.ProtectedRecordCount);
            Assert.Equal(0, result.LegacyRecordCount);
            Assert.NotNull(result.ChainHeadHash);
            Assert.Equal(
                64,
                result.ChainHeadHash!.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static SecurityAuditRecord CreateRecord(
        string resourceId,
        SecurityAuditOutcome outcome)
    {
        return new SecurityAuditRecord
        {
            Operation = "artifact.read",
            ResourceType = "Artifact",
            ResourceId = resourceId,
            SubjectId = "security-test",
            Outcome = outcome,
            OccurredUtc =
                new DateTimeOffset(
                    2026,
                    8,
                    16,
                    20,
                    0,
                    0,
                    TimeSpan.Zero),
            Facts =
                new Dictionary<string, string>
                {
                    ["correlationId"] =
                        $"correlation-{resourceId}"
                }
        };
    }
}
