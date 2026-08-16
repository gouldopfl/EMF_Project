using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class
    SecurityAuditLegacyIntegrityMigrationTests
{
    [Fact]
    public async Task InitializeAsync_preserves_legacy_rows()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "legacy-audit.db");

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
                    CREATE TABLE SecurityAudit_SchemaMigrations (
                        Version INTEGER PRIMARY KEY,
                        Name TEXT NOT NULL,
                        AppliedUtc TEXT NOT NULL
                    );

                    INSERT INTO SecurityAudit_SchemaMigrations
                        (Version, Name, AppliedUtc)
                    VALUES
                        (1, 'InitialSecurityAuditSchema',
                         '2026-08-14T12:00:00+00:00');

                    CREATE TABLE SecurityAuditRecords (
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

                    INSERT INTO SecurityAuditRecords (
                        Operation,
                        ResourceType,
                        ResourceId,
                        SubjectId,
                        Outcome,
                        OccurredUtc,
                        FactsJson
                    )
                    VALUES (
                        'artifact.read',
                        'Artifact',
                        'legacy-001',
                        'legacy-user',
                        'Succeeded',
                        '2026-08-14T12:00:00+00:00',
                        '{}'
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var schema =
                new SecurityAuditSqliteSchema(
                    databasePath);

            await schema.InitializeAsync();

            var verifier =
                new SqliteSecurityAuditIntegrityVerifier(
                    databasePath);

            var legacyResult =
                await verifier.VerifyAsync();

            Assert.True(legacyResult.IsValid);
            Assert.Equal(
                1,
                legacyResult.LegacyRecordCount);
            Assert.Equal(
                0,
                legacyResult.ProtectedRecordCount);

            var sink =
                new SqliteSecurityAuditSink(
                    databasePath);

            await sink.WriteAsync(
                new SecurityAuditRecord
                {
                    Operation = "artifact.read",
                    ResourceType = "Artifact",
                    ResourceId = "protected-001",
                    SubjectId = "protected-user",
                    Outcome =
                        SecurityAuditOutcome.Succeeded,
                    OccurredUtc =
                        new DateTimeOffset(
                            2026,
                            8,
                            16,
                            22,
                            0,
                            0,
                            TimeSpan.Zero)
                });

            var upgradedResult =
                await verifier.VerifyAsync();

            Assert.True(upgradedResult.IsValid);
            Assert.Equal(
                1,
                upgradedResult.LegacyRecordCount);
            Assert.Equal(
                1,
                upgradedResult.ProtectedRecordCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
