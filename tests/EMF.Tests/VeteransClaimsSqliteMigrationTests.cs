using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteMigrationTests
{
    [Fact]
    public async Task InitializeAsync_RecordsCurrentMigrationsOnce()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var schema =
                new VeteransClaimsSqliteSchema(
                    databasePath);

            await schema.InitializeAsync();
            await schema.InitializeAsync();

            var builder =
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath
                };

            await using var connection =
                new SqliteConnection(
                    builder.ToString());

            await connection.OpenAsync();

            await using (
                var tableCommand =
                    connection.CreateCommand())
            {
                tableCommand.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN (
                          'VeteransClaims_ClaimedConditions',
                          'VeteransClaims_MedicalConditions',
                          'VeteransClaims_VeteranMedicalConditions',
                          'VeteransClaims_ClaimedConditionMedicalConditions',
                          'VeteransClaims_ServiceConnectionTheories',
                          'VeteransClaims_ServiceConnectionBases'
                      );
                    """;

                Assert.Equal(
                    6,
                    Convert.ToInt32(
                        await tableCommand
                            .ExecuteScalarAsync()));
            }

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT Version, Name, AppliedUtc
                FROM VeteransClaims_SchemaMigrations
                ORDER BY Version;
                """;

            await using var reader =
                await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));

            Assert.Equal(
                "InitialVeteransClaimsSchema",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));

            Assert.Equal(
                "AddServiceEventsAndExposures",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetInt32(0));

            Assert.Equal(
                "AddClaimedAndMedicalConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(4, reader.GetInt32(0));

            Assert.Equal(
                "AddVeteranMedicalConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(5, reader.GetInt32(0));

            Assert.Equal(
                "AddClaimedConditionMedicalConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(6, reader.GetInt32(0));

            Assert.Equal(
                "AddServiceConnectionTheoriesAndBases",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MigrateAsync_RejectsNewerDatabaseVersion()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var migrations =
                new[]
                {
                    new VeteransClaimsSqliteMigration(
                        1,
                        "InitialTestSchema",
                        """
                        CREATE TABLE TestRecords (
                            Id TEXT PRIMARY KEY
                        );
                        """)
                };

            var migrator =
                new VeteransClaimsSqliteMigrator(
                    databasePath,
                    migrations);

            await migrator.MigrateAsync();

            var builder =
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath
                };

            await using (
                var connection =
                    new SqliteConnection(
                        builder.ToString()))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    INSERT INTO
                        VeteransClaims_SchemaMigrations (
                            Version,
                            Name,
                            AppliedUtc
                        )
                    VALUES (
                        2,
                        'FutureMigration',
                        $appliedUtc
                    );
                    """;

                command.Parameters.AddWithValue(
                    "$appliedUtc",
                    DateTimeOffset.UtcNow.ToString("O"));

                await command.ExecuteNonQueryAsync();
            }

            var exception =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => migrator.MigrateAsync());

            Assert.Contains(
                "unsupported migration version 2",
                exception.Message);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MigrateAsync_RollsBackFailedMigration()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var migrations =
                new[]
                {
                    new VeteransClaimsSqliteMigration(
                        1,
                        "InitialTestSchema",
                        """
                        CREATE TABLE FirstRecords (
                            Id TEXT PRIMARY KEY
                        );
                        """),
                    new VeteransClaimsSqliteMigration(
                        2,
                        "FailingTestMigration",
                        """
                        CREATE TABLE SecondRecords (
                            Id TEXT PRIMARY KEY
                        );

                        INSERT INTO MissingTable (Id)
                        VALUES ('failure');
                        """)
                };

            var migrator =
                new VeteransClaimsSqliteMigrator(
                    databasePath,
                    migrations);

            await Assert.ThrowsAsync<SqliteException>(
                () => migrator.MigrateAsync());

            var builder =
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath
                };

            await using var connection =
                new SqliteConnection(
                    builder.ToString());

            await connection.OpenAsync();

            await using var tableCommand =
                connection.CreateCommand();

            tableCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'SecondRecords';
                """;

            Assert.Equal(
                0,
                Convert.ToInt32(
                    await tableCommand.ExecuteScalarAsync()));

            await using var ledgerCommand =
                connection.CreateCommand();

            ledgerCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM VeteransClaims_SchemaMigrations;
                """;

            Assert.Equal(
                1,
                Convert.ToInt32(
                    await ledgerCommand.ExecuteScalarAsync()));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MigrateAsync_AppliesNewMigrationAfterExistingVersion()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var firstMigration =
                new VeteransClaimsSqliteMigration(
                    1,
                    "CreateTestRecords",
                    """
                    CREATE TABLE TestRecords (
                        Id TEXT PRIMARY KEY
                    );
                    """);

            await new VeteransClaimsSqliteMigrator(
                databasePath,
                new[] { firstMigration })
                .MigrateAsync();

            var secondMigration =
                new VeteransClaimsSqliteMigration(
                    2,
                    "AddTestRecordDescription",
                    """
                    ALTER TABLE TestRecords
                    ADD COLUMN Description TEXT;
                    """);

            await new VeteransClaimsSqliteMigrator(
                databasePath,
                new[]
                {
                    firstMigration,
                    secondMigration
                })
                .MigrateAsync();

            var builder =
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath
                };

            await using var connection =
                new SqliteConnection(
                    builder.ToString());

            await connection.OpenAsync();

            await using var migrationCommand =
                connection.CreateCommand();

            migrationCommand.CommandText =
                """
                SELECT Version
                FROM VeteransClaims_SchemaMigrations
                ORDER BY Version;
                """;

            var versions = new List<int>();

            await using (
                var reader =
                    await migrationCommand
                        .ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    versions.Add(reader.GetInt32(0));
                }
            }

            Assert.Equal(new[] { 1, 2 }, versions);

            await using var columnCommand =
                connection.CreateCommand();

            columnCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM pragma_table_info('TestRecords')
                WHERE name = 'Description';
                """;

            Assert.Equal(
                1,
                Convert.ToInt32(
                    await columnCommand
                        .ExecuteScalarAsync()));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
