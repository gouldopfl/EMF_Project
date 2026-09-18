using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsMedicalLiteratureBasisMigrationTests
{
    [Fact]
    public async Task Migrate87_FansOutLegacyLiteratureAcrossLinkedBases()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await InitializeThrough86Async(databasePath);
            await SeedLegacyLiteratureAsync(
                databasePath,
                includeBasisRequirements: true);

            await new VeteransClaimsSqliteMigrator(
                databasePath,
                VeteransClaimsSqliteMigrations.All)
                .MigrateAsync();

            await using var connection = CreateConnection(databasePath);
            await connection.OpenAsync();

            var associationBases =
                await ReadBasisIdsAsync(
                    connection,
                    "VeteransClaims_RequirementMedicalLiterature");

            Assert.Equal(
                new[] { "basis-a", "basis-b" },
                associationBases);

            var reviewedBases =
                await ReadBasisIdsAsync(
                    connection,
                    "VeteransClaims_ReviewedMedicalLiteratureClassifications");

            Assert.Equal(
                new[] { "basis-a", "basis-b" },
                reviewedBases);

            var excerptBases =
                await ReadBasisIdsAsync(
                    connection,
                    "VeteransClaims_ReviewedMedicalLiteratureExcerpts");

            Assert.Equal(
                new[] { "basis-a", "basis-b" },
                excerptBases);

            await using var migrationCommand =
                connection.CreateCommand();

            migrationCommand.CommandText =
                """
                SELECT Name
                FROM VeteransClaims_SchemaMigrations
                WHERE Version = 87;
                """;

            Assert.Equal(
                "ScopeMedicalLiteratureToServiceConnectionBasis",
                Assert.IsType<string>(
                    await migrationCommand.ExecuteScalarAsync()));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Migrate87_RejectsLegacyLiteratureWithoutLinkedBasis()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await InitializeThrough86Async(databasePath);
            await SeedLegacyLiteratureAsync(
                databasePath,
                includeBasisRequirements: false);

            var migrator =
                new VeteransClaimsSqliteMigrator(
                    databasePath,
                    VeteransClaimsSqliteMigrations.All);

            await Assert.ThrowsAsync<SqliteException>(
                () => migrator.MigrateAsync());

            await using var connection = CreateConnection(databasePath);
            await connection.OpenAsync();

            await using (var migrationCommand =
                connection.CreateCommand())
            {
                migrationCommand.CommandText =
                    """
                    SELECT MAX(Version)
                    FROM VeteransClaims_SchemaMigrations;
                    """;

                Assert.Equal(
                    86L,
                    Convert.ToInt64(
                        await migrationCommand.ExecuteScalarAsync()));
            }

            await using var columnCommand =
                connection.CreateCommand();

            columnCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM pragma_table_info(
                    'VeteransClaims_RequirementMedicalLiterature')
                WHERE name = 'ServiceConnectionBasisId';
                """;

            Assert.Equal(
                0,
                Convert.ToInt32(
                    await columnCommand.ExecuteScalarAsync()));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData("basis-a")]
    [InlineData("basis-b")]
    public async Task Repository_ReadsOnlyRequestedBasisAndItsExcerpts(string basisValue)
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await InitializeThrough86Async(databasePath);
            await SeedLegacyLiteratureAsync(databasePath, true);
            var repository = new EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories
                .SqliteMedicalLiteratureRepository(databasePath);
            await repository.InitializeAsync();
            var basis = new EMF.Extensions.VeteransClaims.Models.Identities.ServiceConnectionBasisId(basisValue);
            var requirement = new EMF.Extensions.VeteransClaims.Models.Identities.RequirementId("requirement-1");

            await using var connection = CreateConnection(databasePath);
            await connection.OpenAsync();
            await using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE VeteransClaims_ReviewedMedicalLiteratureExcerpts
                SET Text = ServiceConnectionBasisId;
                """;
            await update.ExecuteNonQueryAsync();

            Assert.Equal(2, (await repository.GetRequirementMedicalLiteratureAsync(requirement)).Count);
            Assert.Equal(basis, Assert.Single(await repository.GetRequirementMedicalLiteratureAsync(basis, requirement)).ServiceConnectionBasisId);
            Assert.Equal(basis, Assert.Single(await repository.GetActiveRequirementMedicalLiteratureAsync(basis, requirement)).ServiceConnectionBasisId);
            var reviewed = Assert.Single(await repository.GetReviewedClassificationsAsync(basis, requirement));
            Assert.Equal(basis, reviewed.Association.ServiceConnectionBasisId);
            Assert.Equal(basisValue, Assert.Single(reviewed.SourceExcerpts).Text);
            Assert.Equal(basis, Assert.Single(await repository.GetReviewedClassificationsAsync(basis,
                new EMF.Core.Models.Identities.ArtifactId("artifact-1"))).Association.ServiceConnectionBasisId);
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task Repository_ReadsSingleBasisWithoutReturningOtherBasisReview()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await InitializeThrough86Async(databasePath);
            await SeedLegacyLiteratureAsync(databasePath, true);
            await using var connection = CreateConnection(databasePath);
            await connection.OpenAsync();
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM VeteransClaims_BasisRequirements WHERE ServiceConnectionBasisId = 'basis-b';";
            await delete.ExecuteNonQueryAsync();
            var repository = new EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories
                .SqliteMedicalLiteratureRepository(databasePath);
            await repository.InitializeAsync();
            var a = new EMF.Extensions.VeteransClaims.Models.Identities.ServiceConnectionBasisId("basis-a");
            var b = new EMF.Extensions.VeteransClaims.Models.Identities.ServiceConnectionBasisId("basis-b");
            var requirement = new EMF.Extensions.VeteransClaims.Models.Identities.RequirementId("requirement-1");
            Assert.Single(await repository.GetRequirementMedicalLiteratureAsync(a, requirement));
            Assert.Empty(await repository.GetRequirementMedicalLiteratureAsync(b, requirement));
            Assert.Empty(await repository.GetActiveRequirementMedicalLiteratureAsync(b, requirement));
            Assert.Empty(await repository.GetReviewedClassificationsAsync(b, requirement));
        }
        finally { File.Delete(databasePath); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing-basis")]
    public async Task Repository_RejectsAmbiguousOrInvalidWrite(string? basisValue)
    {
        var path = CreateDatabasePath();
        try
        {
            await InitializeThrough86Async(path);
            await SeedLegacyLiteratureAsync(path, true);
            var repository = new SqliteMedicalLiteratureRepository(path);
            await repository.InitializeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.AddRequirementMedicalLiteratureAsync(new RequirementMedicalLiterature
                {
                    ServiceConnectionBasisId = basisValue is null ? null : new ServiceConnectionBasisId(basisValue),
                    RequirementId = new("requirement-1"),
                    MedicalLiteratureSourceId = new("source-1"),
                    GuidanceRole = "Clarifies",
                    Description = "New association"
                }));
            Assert.Equal(2, (await repository.GetRequirementMedicalLiteratureAsync(new RequirementId("requirement-1"))).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Repository_WritesExplicitBasisWithIndependentDescription()
    {
        var path = CreateDatabasePath();
        try
        {
            await InitializeThrough86Async(path);
            await SeedLegacyLiteratureAsync(path, true);
            var repository = new SqliteMedicalLiteratureRepository(path);
            await repository.InitializeAsync();
            foreach (var value in new[] { "basis-a", "basis-b" })
                await repository.AddRequirementMedicalLiteratureAsync(new RequirementMedicalLiterature
                {
                    ServiceConnectionBasisId = new(value),
                    RequirementId = new("requirement-1"),
                    MedicalLiteratureSourceId = new("source-1"),
                    GuidanceRole = "Clarifies",
                    Description = value
                });
            foreach (var value in new[] { "basis-a", "basis-b" })
            {
                var rows = await repository.GetRequirementMedicalLiteratureAsync(new ServiceConnectionBasisId(value), new RequirementId("requirement-1"));
                Assert.Equal(value, Assert.Single(rows.Where(x => x.GuidanceRole == "Clarifies")).Description);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Repository_ReviewAcceptanceAndExcerptsAreIndependentAcrossBases()
    {
        var path = CreateDatabasePath();
        try
        {
            await InitializeThrough86Async(path);
            await SeedLegacyLiteratureAsync(path, true);
            var repository = new SqliteMedicalLiteratureRepository(path);
            await repository.InitializeAsync();
            var requirement = new RequirementId("requirement-1");
            var a = new ServiceConnectionBasisId("basis-a");
            var b = new ServiceConnectionBasisId("basis-b");
            await repository.AddReviewedClassificationAsync(CreateReview(a, "shared-correlation", "A accepted excerpt"));
            Assert.DoesNotContain(await repository.GetReviewedClassificationsAsync(b, requirement), x => x.CorrelationId == "shared-correlation");
            await repository.AddReviewedClassificationAsync(CreateReview(b, "shared-correlation", "B accepted excerpt"));
            foreach (var (basis, expected) in new[] { (a, "A accepted excerpt"), (b, "B accepted excerpt") })
            {
                var review = Assert.Single((await repository.GetReviewedClassificationsAsync(basis, requirement))
                    .Where(x => x.CorrelationId == "shared-correlation"));
                Assert.Equal(basis, review.Association.ServiceConnectionBasisId);
                Assert.Equal(expected, Assert.Single(review.SourceExcerpts).Text);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddReviewedClassificationAsync(
                CreateReview(a, "duplicate", "A accepted excerpt")));
            Assert.Equal(2, (await repository.GetReviewedClassificationsAsync(b, requirement)).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Repository_RejectsMissingRequirementAndDefaultBasis()
    {
        var path = CreateDatabasePath();
        try
        {
            var repository = new SqliteMedicalLiteratureRepository(path);
            await repository.InitializeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ResolveServiceConnectionBasisAsync(new("missing")));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.ResolveServiceConnectionBasisAsync(new("missing"), default(ServiceConnectionBasisId)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.GetRequirementMedicalLiteratureAsync(default(ServiceConnectionBasisId), new("missing")));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.GetReviewedClassificationsAsync(default(ServiceConnectionBasisId), new RequirementId("missing")));
        }
        finally { File.Delete(path); }
    }

    private static ReviewedMedicalLiteratureClassification CreateReview(
        ServiceConnectionBasisId basis, string correlation, string text,
        string role = "Clarifies")
    {
        var now = DateTimeOffset.UtcNow;
        return new ReviewedMedicalLiteratureClassification
        {
            Association = new RequirementMedicalLiterature
            {
                ServiceConnectionBasisId = basis,
                RequirementId = new("requirement-1"),
                MedicalLiteratureSourceId = new("source-1"),
                GuidanceRole = role,
                Description = text
            },
            ArtifactId = new("artifact-1"),
            PromotedBy = "Reviewer", PromotedUtc = now,
            ReviewedBy = "Reviewer", ReviewedUtc = now,
            IntelligenceOutput = "Accepted", CapabilityId = "human.review",
            ProviderId = "human", CorrelationId = correlation,
            EngineName = "human-review", StartedUtc = now, CompletedUtc = now,
            RequiresReview = false, Warnings = [],
            SourceExcerpts = [new() { ArtifactId = new("artifact-1"), Text = text, StartOffset = 0, Length = text.Length }]
        };
    }

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.db");

    internal static async Task InitializeThrough86Async(
        string databasePath)
    {
        var migrations =
            VeteransClaimsSqliteMigrations.All
                .Where(migration => migration.Version <= 86)
                .ToArray();

        await new VeteransClaimsSqliteMigrator(
            databasePath,
            migrations)
            .MigrateAsync();
    }

    private static SqliteConnection CreateConnection(
        string databasePath)
    {
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            };

        return new SqliteConnection(builder.ToString());
    }

    private static async Task<IReadOnlyList<string>>
        ReadBasisIdsAsync(
            SqliteConnection connection,
            string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT ServiceConnectionBasisId
            FROM {tableName}
            ORDER BY ServiceConnectionBasisId;
            """;

        var basisIds = new List<string>();

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            basisIds.Add(reader.GetString(0));

        return basisIds;
    }

    internal static async Task SeedLegacyLiteratureAsync(
        string databasePath,
        bool includeBasisRequirements)
    {
        await using var connection = CreateConnection(databasePath);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Veterans (Id)
            VALUES ('veteran-1');

            INSERT INTO VeteransClaims_Claims (Id, VeteranId)
            VALUES ('claim-1', 'veteran-1');

            INSERT INTO VeteransClaims_ClaimIssues (
                Id, ClaimId, ClaimIssueType
            )
            VALUES ('issue-1', 'claim-1', 'Secondary');

            INSERT INTO VeteransClaims_ServiceConnectionTheories (
                Id, ClaimIssueId, TheoryType
            )
            VALUES ('theory-1', 'issue-1', 'Secondary');

            INSERT INTO VeteransClaims_ServiceConnectionBases (
                Id, ClaimIssueId, ServiceConnectionTheoryId
            )
            VALUES
                ('basis-a', 'issue-1', 'theory-1'),
                ('basis-b', 'issue-1', 'theory-1');

            INSERT INTO VeteransClaims_RegulatoryAuthorities (
                Id, AuthorityType, Citation, Title
            )
            VALUES ('authority-1', 'CFR', '38 CFR', 'Test authority');

            INSERT INTO VeteransClaims_RegulatoryProvisions (
                Id, RegulatoryAuthorityId, ProvisionType, Citation
            )
            VALUES ('provision-1', 'authority-1', 'Rule', '3.310');

            INSERT INTO VeteransClaims_Requirements (
                Id, RegulatoryProvisionId, Description
            )
            VALUES ('requirement-1', 'provision-1', 'Medical nexus');

            INSERT INTO VeteransClaims_MedicalLiteratureSources (
                Id, Title, Authors, Publication,
                VaAffiliated, VaFunded, PeerReviewed
            )
            VALUES (
                'source-1', 'Test literature', 'Author', 'Journal',
                0, 0, 1
            );

            INSERT INTO VeteransClaims_MedicalLiteratureSourceArtifacts (
                MedicalLiteratureSourceId, ArtifactId
            )
            VALUES ('source-1', 'artifact-1');

            INSERT INTO VeteransClaims_RequirementMedicalLiterature (
                RequirementId, MedicalLiteratureSourceId,
                GuidanceRole, Description
            )
            VALUES (
                'requirement-1', 'source-1',
                'Corroborates', 'Legacy association'
            );

            INSERT INTO
                VeteransClaims_ReviewedMedicalLiteratureClassifications (
                RequirementId, MedicalLiteratureSourceId, GuidanceRole,
                ArtifactId, Description, PromotedBy, PromotedUtc,
                ReviewedBy, ReviewedUtc, IntelligenceOutput,
                CapabilityId, ProviderId, CorrelationId, EngineName,
                StartedUtc, CompletedUtc, RequiresReview, WarningsJson
            )
            VALUES (
                'requirement-1', 'source-1', 'Corroborates',
                'artifact-1', 'Reviewed legacy association',
                'promoter', '2026-09-18T12:00:00Z',
                'reviewer', '2026-09-18T12:01:00Z',
                'output', 'capability', 'provider', 'correlation-1',
                'engine', '2026-09-18T11:59:00Z',
                '2026-09-18T12:00:00Z', 0, '[]'
            );

            INSERT INTO VeteransClaims_ReviewedMedicalLiteratureExcerpts (
                RequirementId, MedicalLiteratureSourceId, GuidanceRole,
                ArtifactId, CorrelationId, ExcerptOrdinal, Text
            )
            VALUES (
                'requirement-1', 'source-1', 'Corroborates',
                'artifact-1', 'correlation-1', 0, 'Excerpt text'
            );
            """;

        await command.ExecuteNonQueryAsync();

        if (!includeBasisRequirements)
            return;

        await using var basisCommand = connection.CreateCommand();
        basisCommand.CommandText =
            """
            INSERT INTO VeteransClaims_BasisRequirements (
                ServiceConnectionBasisId, RequirementId
            )
            VALUES
                ('basis-a', 'requirement-1'),
                ('basis-b', 'requirement-1');
            """;

        await basisCommand.ExecuteNonQueryAsync();
    }
}
