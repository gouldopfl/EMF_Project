using EMF.Security.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SecurityAuditSqliteMigrationTests
{
    [Fact]
    public async Task InitializeAsync_rejects_newer_schema_version()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "future-audit.db");

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
                    CREATE TABLE
                        SecurityAudit_SchemaMigrations (
                            Version INTEGER PRIMARY KEY,
                            Name TEXT NOT NULL,
                            AppliedUtc TEXT NOT NULL
                        );

                    INSERT INTO SecurityAudit_SchemaMigrations (
                        Version,
                        Name,
                        AppliedUtc
                    )
                    VALUES (
                        3,
                        'FutureSecurityAuditSchemaV3',
                        '2026-08-14T12:00:00.0000000+00:00'
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var schema =
                new SecurityAuditSqliteSchema(
                    databasePath);

            var exception =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => schema.InitializeAsync());

            Assert.Contains(
                "unsupported migration version 3",
                exception.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_does_not_record_failed_migration()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "failed-audit.db");

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
                    CREATE TABLE SecurityAuditRecords (
                        Id INTEGER PRIMARY KEY
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var schema =
                new SecurityAuditSqliteSchema(
                    databasePath);

            await Assert.ThrowsAsync<SqliteException>(
                () => schema.InitializeAsync());

            await using var verification =
                new SqliteConnection(
                    $"Data Source={databasePath}");

            await verification.OpenAsync();

            await using var ledgerCommand =
                verification.CreateCommand();

            ledgerCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM SecurityAudit_SchemaMigrations;
                """;

            var appliedCount =
                Convert.ToInt32(
                    await ledgerCommand.ExecuteScalarAsync());

            Assert.Equal(0, appliedCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
