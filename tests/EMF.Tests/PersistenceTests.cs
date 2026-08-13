using EMF.Persistence.Repositories;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class PersistenceTests
{

    [Fact]
    public async Task SqliteEvidenceRepository_AddArtifactWithProvenanceAsync_RollsBackOnFailure()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-evidence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);
            await repository.InitializeAsync();

            await using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TRIGGER fail_provenance
                    BEFORE INSERT ON Provenance
                    BEGIN
                        SELECT RAISE(ABORT, 'forced provenance failure');
                    END;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var artifactId =
                new ArtifactId("artifact-rollback-001");

            var artifact =
                new Artifact
                {
                    Id = artifactId,
                    Name = "rollback.db",
                    ArtifactType = "file"
                };

            var provenance =
                new Provenance
                {
                    ArtifactId = artifactId,
                    Source = "/data/rollback.db",
                    RecordedBy = "EMF.Tests"
                };

            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
                () => repository.AddArtifactWithProvenanceAsync(
                    artifact,
                    provenance));

            var stored =
                await repository.GetArtifactAsync(artifactId);

            Assert.Null(stored);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_InitializeAsync_CreatesSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            Assert.True(File.Exists(databasePath));

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Artifacts', 'Relationships', 'Provenance');
                """;

            var tables = new List<string>();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Contains("Artifacts", tables);
            Assert.Contains("Relationships", tables);
            Assert.Contains("Provenance", tables);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task SqliteEvidenceRepository_ArtifactRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var createdUtc = DateTimeOffset.UtcNow;

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-001"),
                Name = "oscar.db",
                ArtifactType = "file",
                CreatedUtc = createdUtc,
                Fingerprint = new ContentFingerprint
                {
                    Algorithm = "SHA-256",
                    Value = "ABC123"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["extension"] = ".db"
                }
            };

            await repository.AddArtifactAsync(artifact);

            var result = await repository.GetArtifactAsync(artifact.Id);

            Assert.NotNull(result);
            Assert.Equal(artifact.Id, result!.Id);
            Assert.Equal("oscar.db", result.Name);
            Assert.Equal("file", result.ArtifactType);
            Assert.Equal(createdUtc, result.CreatedUtc);
            Assert.Equal(artifact.Fingerprint, result.Fingerprint);

            var extension =
                Assert.IsType<JsonElement>(result.Metadata["extension"]);

            Assert.Equal(".db", extension.GetString());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_RelationshipRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var relationship = new Relationship
            {
                SourceArtifactId = new ArtifactId("artifact-a"),
                TargetArtifactId = new ArtifactId("artifact-b"),
                RelationshipType = RelationshipTypes.Contains,
                Properties = new Dictionary<string, object>
                {
                    ["role"] = "child"
                }
            };

            await repository.AddRelationshipAsync(relationship);

            var results = await repository.GetRelationshipsAsync(
                relationship.SourceArtifactId);

            var result = Assert.Single(results);

            Assert.Equal(
                relationship.SourceArtifactId,
                result.SourceArtifactId);

            Assert.Equal(
                relationship.TargetArtifactId,
                result.TargetArtifactId);

            Assert.Equal(
                RelationshipTypes.Contains,
                result.RelationshipType);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_ProvenanceRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var provenance = new Provenance
            {
                ArtifactId = new ArtifactId("artifact-001"),
                Source = "/data/oscar.db",
                RecordedBy = "EMF.Discovery",
                Properties = new Dictionary<string, object>
                {
                    ["sourceType"] = "file"
                }
            };

            await repository.AddProvenanceAsync(provenance);

            var results = await repository.GetProvenanceAsync(
                provenance.ArtifactId);

            var result = Assert.Single(results);

            Assert.Equal(provenance.ArtifactId, result.ArtifactId);
            Assert.Equal("/data/oscar.db", result.Source);
            Assert.Equal("EMF.Discovery", result.RecordedBy);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

}
