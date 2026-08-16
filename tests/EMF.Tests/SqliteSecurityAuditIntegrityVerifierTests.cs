using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class
    SqliteSecurityAuditIntegrityVerifierTests
{
    [Fact]
    public async Task VerifyAsync_detects_record_tampering()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "audit-verifier.db");

            var sink =
                new SqliteSecurityAuditSink(
                    databasePath);

            await sink.InitializeAsync();

            await sink.WriteAsync(
                CreateRecord("artifact-001"));

            await sink.WriteAsync(
                CreateRecord("artifact-002"));

            var verifier =
                new SqliteSecurityAuditIntegrityVerifier(
                    databasePath);

            var validResult =
                await verifier.VerifyAsync();

            Assert.True(validResult.IsValid);
            Assert.Equal(
                2,
                validResult.ProtectedRecordCount);
            Assert.Equal(
                0,
                validResult.LegacyRecordCount);
            Assert.Equal(
                2,
                validResult.LastProtectedRecordId);
            Assert.NotNull(
                validResult.ChainHeadHash);
            Assert.Equal(
                64,
                validResult.ChainHeadHash!.Length);


            await using (
                var connection =
                    new SqliteConnection(
                        $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    UPDATE SecurityAuditRecords
                    SET ResourceId = 'tampered'
                    WHERE Id = 1;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var invalidResult =
                await verifier.VerifyAsync();

            Assert.False(invalidResult.IsValid);
            Assert.Equal(
                1,
                invalidResult.InvalidRecordId);
            Assert.Contains(
                "does not match its content",
                invalidResult.FailureReason);

            await using (
                var connection =
                    new SqliteConnection(
                        $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    UPDATE SecurityAuditRecords
                    SET ResourceId = 'artifact-001'
                    WHERE Id = 1;

                    DELETE FROM SecurityAuditRecords
                    WHERE Id = 1;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var deletionResult =
                await verifier.VerifyAsync();

            Assert.False(deletionResult.IsValid);
            Assert.Equal(
                2,
                deletionResult.InvalidRecordId);
            Assert.Contains(
                "Previous audit record hash does not match",
                deletionResult.FailureReason);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static SecurityAuditRecord CreateRecord(
        string resourceId)
    {
        return new SecurityAuditRecord
        {
            Operation = "artifact.read",
            ResourceType = "Artifact",
            ResourceId = resourceId,
            SubjectId = "integrity-test",
            Outcome = SecurityAuditOutcome.Succeeded,
            OccurredUtc =
                new DateTimeOffset(
                    2026,
                    8,
                    16,
                    21,
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
