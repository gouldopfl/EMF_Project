using EMF.Extensions.VeteransClaims.Models.Adjudication;
using System.Text.Json;
using EMF.Common;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerBuildManifestArchiveTests
{
    [Fact]
    public async Task BuildManifestArchive_RoundTripsAndDeduplicatesExactRetry()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var repository = new SqliteEvidencePackageRepository(path);

            var manifest = EmfBuildManifestIdentity.Capture(
                typeof(EmfAssemblyBuildIdentity).Assembly);
            var document = ReviewerBuildManifestDocument.Parse(
                JsonSerializer.Serialize(manifest));

            await repository.SaveBuildManifestAsync(document);
            await repository.SaveBuildManifestAsync(document);

            var stored =
                await repository.GetBuildManifestAsync(manifest.BuildId);

            Assert.NotNull(stored);
            Assert.Equal(manifest.BuildId, stored!.BuildId);
            Assert.Equal(
                manifest.SourceRevisionId,
                stored.SourceRevisionId);
            stored.ValidateIntegrity();
            Assert.Equal(document.Json, stored.Json);

            await using var connection =
                new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM VeteransClaims_ReviewerBuildManifests
                WHERE BuildId = $buildId;
                """;
            command.Parameters.AddWithValue(
                "$buildId",
                manifest.BuildId);

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await command.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildManifestArchive_IsImmutable()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var repository = new SqliteEvidencePackageRepository(path);

            var manifest = EmfBuildManifestIdentity.Capture(
                typeof(EmfAssemblyBuildIdentity).Assembly);
            var document = ReviewerBuildManifestDocument.Parse(
                JsonSerializer.Serialize(manifest));

            await repository.SaveBuildManifestAsync(document);

            await Assert.ThrowsAsync<SqliteException>(
                () => ExecuteAsync(
                    path,
                    "UPDATE VeteransClaims_ReviewerBuildManifests " +
                    "SET ArchivedUtc='2027-01-01T00:00:00Z';"));

            await Assert.ThrowsAsync<SqliteException>(
                () => ExecuteAsync(
                    path,
                    "DELETE FROM VeteransClaims_ReviewerBuildManifests;"));

            await Assert.ThrowsAsync<SqliteException>(
                () => ExecuteAsync(
                    path,
                    "PRAGMA recursive_triggers=OFF; " +
                    "INSERT OR REPLACE INTO VeteransClaims_ReviewerBuildManifests " +
                    "(BuildId,ManifestJson,ArchivedUtc) " +
                    "SELECT BuildId,ManifestJson,'2027-01-01T00:00:00Z' " +
                    "FROM VeteransClaims_ReviewerBuildManifests;"));

            var stored =
                await repository.GetBuildManifestAsync(manifest.BuildId);

            Assert.NotNull(stored);
            Assert.Equal(manifest.BuildId, stored!.BuildId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildManifestDocument_RejectsArtifactChangeWithUnchangedBuildId()
    {
        var manifest = EmfBuildManifestIdentity.Capture(
            typeof(EmfAssemblyBuildIdentity).Assembly);
        var changed = manifest with
        {
            Artifacts = manifest.Artifacts
                .Select(artifact => artifact with
                {
                    ByteLength = artifact.ByteLength + 1
                })
                .ToArray()
        };

        Assert.Throws<InvalidDataException>(() =>
            ReviewerBuildManifestDocument.Parse(JsonSerializer.Serialize(changed)));
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("null")]
    [InlineData("{\"Artifacts\":null}")]
    [InlineData("{\"Artifacts\":[null]}")]
    public void BuildManifestDocument_RejectsMalformedOrNullStructure(string json)
    {
        Assert.Throws<InvalidDataException>(() =>
            ReviewerBuildManifestDocument.Parse(json));
    }

    [Fact]
    public async Task BuildManifestArchive_ReadRejectsTamperingWithMatchingEnvelope()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var repository = new SqliteEvidencePackageRepository(path);
            var manifest = EmfBuildManifestIdentity.Capture(
                typeof(EmfAssemblyBuildIdentity).Assembly);
            var changed = manifest with
            {
                Artifacts = manifest.Artifacts
                    .Select(artifact => artifact with
                    {
                        ByteLength = artifact.ByteLength + 1
                    })
                    .ToArray()
            };

            // The envelope IDs match. Only full canonical validation can reject it.
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO VeteransClaims_ReviewerBuildManifests (
                        BuildId, ManifestJson, ArchivedUtc)
                    VALUES ($id, $json, $utc);
                    """;
                insert.Parameters.AddWithValue("$id", manifest.BuildId);
                insert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(changed));
                insert.Parameters.AddWithValue(
                    "$utc",
                    DateTimeOffset.UtcNow.ToString(
                        "O", System.Globalization.CultureInfo.InvariantCulture));
                await insert.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repository.GetBuildManifestAsync(manifest.BuildId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task ExecuteAsync(
        string path,
        string sql)
    {
        await using var connection =
            new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
