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
                          'VeteransClaims_ServiceConnectionBases',
                          'VeteransClaims_BasisClaimedConditions',
                          'VeteransClaims_BasisServiceEvents',
                          'VeteransClaims_BasisExposures',
                          'VeteransClaims_BasisServiceConnectedConditions',
                          'VeteransClaims_BasisPreexistingConditions',
                          'VeteransClaims_RegulatoryAuthorities',
                          'VeteransClaims_RegulatoryProvisions',
                          'VeteransClaims_Requirements',
                          'VeteransClaims_IssueDecisionRegulatoryProvisions',
                          'VeteransClaims_DisabilityEvaluationRegulatoryProvisions',
                          'VeteransClaims_EffectiveDateRegulatoryProvisions',
                          'VeteransClaims_ExposureRegulatoryProvisions',
                          'VeteransClaims_ExposureRequirements',
                          'VeteransClaims_BasisPresumptions',
                          'VeteransClaims_MedicalOpinions',
                          'VeteransClaims_BasisMedicalOpinions',
                          'VeteransClaims_ClaimedConditionMedicalConditionMedicalOpinions',
                          'VeteransClaims_VeteranMedicalConditionMedicalOpinions',
                          'VeteransClaims_EvidenceClassifications',
                          'VeteransClaims_EvidenceClassificationExposures',
                          'VeteransClaims_EvidenceClassificationMedicalConditions',
                          'VeteransClaims_EvidenceClassificationMedicalOpinions',
                          'VeteransClaims_EvidenceClassificationRequirements',
                          'VeteransClaims_EvidenceClassificationServiceEvents',
                          'VeteransClaims_Findings',
                          'VeteransClaims_EvidenceClassificationFindings',
                          'VeteransClaims_FindingRegulatoryProvisions',
                          'VeteransClaims_FindingArtifacts',
                          'VeteransClaims_EvidenceGaps',
                          'VeteransClaims_EvidenceDevelopmentPlans',
                          'VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps',
                          'VeteransClaims_EvidenceDevelopmentPlanRequirements'
                      );
                    """;

                Assert.Equal(
                    38,
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

            Assert.True(await reader.ReadAsync());
            Assert.Equal(7, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisClaimedConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(8, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisServiceEvents",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(9, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisExposures",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(10, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisServiceConnectedConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(11, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisPreexistingConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(12, reader.GetInt32(0));

            Assert.Equal(
                "AddRegulatoryFoundation",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(13, reader.GetInt32(0));

            Assert.Equal(
                "AddIssueDecisionRegulatoryProvisions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(14, reader.GetInt32(0));

            Assert.Equal(
                "AddDisabilityEvaluationRegulatoryProvisions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(15, reader.GetInt32(0));

            Assert.Equal(
                "AddEffectiveDateRegulatoryProvisions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(16, reader.GetInt32(0));

            Assert.Equal(
                "AddExposureRegulatoryProvisions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(17, reader.GetInt32(0));

            Assert.Equal(
                "AddExposureRequirements",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(18, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisPresumptions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(19, reader.GetInt32(0));

            Assert.Equal(
                "AddMedicalOpinions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(20, reader.GetInt32(0));

            Assert.Equal(
                "AddBasisMedicalOpinions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(21, reader.GetInt32(0));

            Assert.Equal(
                "AddClaimedConditionMedicalConditionMedicalOpinions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(22, reader.GetInt32(0));

            Assert.Equal(
                "AddVeteranMedicalConditionMedicalOpinions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(23, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassifications",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(24, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassificationExposures",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(25, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassificationMedicalConditions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(26, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassificationMedicalOpinions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(27, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassificationRequirements",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(28, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassificationServiceEvents",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(29, reader.GetInt32(0));

            Assert.Equal(
                "AddFindings",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(30, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceClassificationFindings",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(31, reader.GetInt32(0));

            Assert.Equal(
                "AddFindingRegulatoryProvisions",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(32, reader.GetInt32(0));

            Assert.Equal(
                "AddFindingArtifacts",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(33, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceGaps",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(34, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceDevelopmentPlans",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(35, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceDevelopmentPlanEvidenceGaps",
                reader.GetString(1));

            Assert.True(
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out _));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(36, reader.GetInt32(0));

            Assert.Equal(
                "AddEvidenceDevelopmentPlanRequirements",
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
