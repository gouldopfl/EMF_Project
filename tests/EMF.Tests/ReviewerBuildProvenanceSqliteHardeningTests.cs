using Microsoft.Data.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerBuildProvenanceSqliteHardeningTests
{
    private const string ParentId = "provenance-parent";
    private static readonly string LinkA = new('A', 64);
    private static readonly string LinkB = new('B', 64);
    private static readonly string BuildA = "sha256:" + new string('A', 64);
    private static readonly string BuildB = "sha256:" + new string('B', 64);
    private static readonly string RevisionA = new('a', 40);
    private static readonly string RevisionB = new('b', 40);

    [Fact]
    public async Task Migration92_RejectsCompositeInsertOrReplaceWithDifferentLinkId()
    {
        await using var connection = await OpenMigration92Async();

        await ExecuteAsync(
            connection,
            $"INSERT INTO VeteransClaims_ReviewerPackageOutputProvenance " +
            $"(ProvenanceId) VALUES ('{ParentId}');");

        await InsertBuildLinkAsync(
            connection,
            LinkA,
            ParentId,
            BuildA,
            RevisionA);

        await ExecuteAsync(
            connection,
            "PRAGMA recursive_triggers=OFF;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            InsertBuildLinkAsync(
                connection,
                LinkB,
                ParentId,
                BuildA,
                RevisionA,
                replace: true));

        await using var verify = connection.CreateCommand();
        verify.CommandText =
            """
            SELECT LinkId
            FROM VeteransClaims_ReviewerPackageOutputBuildProvenance;
            """;

        Assert.Equal(LinkA, await verify.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration92_RejectsOrphanWhenForeignKeysAreDisabled()
    {
        await using var connection = await OpenMigration92Async();

        await ExecuteAsync(
            connection,
            "PRAGMA foreign_keys=OFF;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            InsertBuildLinkAsync(
                connection,
                LinkA,
                "missing-parent",
                BuildA,
                RevisionA));
    }

    [Fact]
    public async Task Migration92_HasNoRowIdReplacementSurface()
    {
        await using var connection = await OpenMigration92Async();

        await ExecuteAsync(
            connection,
            $"INSERT INTO VeteransClaims_ReviewerPackageOutputProvenance " +
            $"(ProvenanceId) VALUES ('{ParentId}');");

        await InsertBuildLinkAsync(
            connection,
            LinkA,
            ParentId,
            BuildA,
            RevisionA);

        await ExecuteAsync(
            connection,
            "PRAGMA recursive_triggers=OFF;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteAsync(
                connection,
                $"""
                INSERT OR REPLACE INTO
                    VeteransClaims_ReviewerPackageOutputBuildProvenance (
                        rowid,
                        LinkId,
                        ProvenanceId,
                        Version,
                        BuildId,
                        SourceRevisionId,
                        LinkedUtc)
                VALUES (
                    1,
                    '{LinkB}',
                    '{ParentId}',
                    1,
                    '{BuildB}',
                    '{RevisionB}',
                    '2026-09-29T17:00:00.0000000+00:00');
                """));
    }

    [Fact]
    public async Task Migration93_HasNoRowIdReplacementSurface()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var migration =
            Assert.Single(
                VeteransClaimsSqliteMigrations.All,
                item => item.Version == 93);

        await ExecuteAsync(connection, migration.Sql);

        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO VeteransClaims_ReviewerBuildManifests (
                BuildId,
                ManifestJson,
                ArchivedUtc)
            VALUES (
                '{BuildA}',
                'manifest-json',
                '2026-09-29T17:00:00.0000000+00:00');
            """);

        await ExecuteAsync(
            connection,
            "PRAGMA recursive_triggers=OFF;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteAsync(
                connection,
                $"""
                INSERT OR REPLACE INTO
                    VeteransClaims_ReviewerBuildManifests (
                        rowid,
                        BuildId,
                        ManifestJson,
                        ArchivedUtc)
                VALUES (
                    1,
                    '{BuildB}',
                    'manifest-json',
                    '2026-09-29T18:00:00.0000000+00:00');
                """));
    }

    private static async Task<SqliteConnection> OpenMigration92Async()
    {
        var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE VeteransClaims_ReviewerPackageOutputProvenance (
                ProvenanceId TEXT PRIMARY KEY NOT NULL
            );
            """);

        var migration =
            Assert.Single(
                VeteransClaimsSqliteMigrations.All,
                item => item.Version == 92);

        await ExecuteAsync(connection, migration.Sql);
        return connection;
    }

    private static Task InsertBuildLinkAsync(
        SqliteConnection connection,
        string linkId,
        string provenanceId,
        string buildId,
        string revision,
        bool replace = false) =>
        ExecuteAsync(
            connection,
            $"""
            INSERT {(replace ? "OR REPLACE " : string.Empty)}INTO
                VeteransClaims_ReviewerPackageOutputBuildProvenance (
                    LinkId,
                    ProvenanceId,
                    Version,
                    BuildId,
                    SourceRevisionId,
                    LinkedUtc)
            VALUES (
                '{linkId}',
                '{provenanceId}',
                1,
                '{buildId}',
                '{revision}',
                '2026-09-29T17:00:00.0000000+00:00');
            """);

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
