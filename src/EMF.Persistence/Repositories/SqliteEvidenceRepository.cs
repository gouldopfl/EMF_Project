using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace EMF.Persistence.Repositories;

public sealed class SqliteEvidenceRepository : IEvidenceRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        };

        return new SqliteConnection(builder.ToString());
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Artifacts (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ArtifactType TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                FingerprintAlgorithm TEXT NULL,
                FingerprintValue TEXT NULL,
                MetadataJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Relationships (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceArtifactId TEXT NOT NULL,
                TargetArtifactId TEXT NOT NULL,
                RelationshipType TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                PropertiesJson TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddArtifactAsync(
        Artifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO Artifacts " +
            "(Id, Name, ArtifactType, CreatedUtc, FingerprintAlgorithm, FingerprintValue, MetadataJson) " +
            "VALUES ($id, $name, $artifactType, $createdUtc, $fingerprintAlgorithm, $fingerprintValue, $metadataJson);";

        command.Parameters.AddWithValue("$id", artifact.Id.Value);
        command.Parameters.AddWithValue("$name", artifact.Name);
        command.Parameters.AddWithValue("$artifactType", artifact.ArtifactType);
        command.Parameters.AddWithValue("$createdUtc", artifact.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$fingerprintAlgorithm",
            (object?)artifact.Fingerprint?.Algorithm ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$fingerprintValue",
            (object?)artifact.Fingerprint?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$metadataJson",
            JsonSerializer.Serialize(artifact.Metadata));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task AddRelationshipAsync(
        Relationship relationship,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public async Task<Artifact?> GetArtifactAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, ArtifactType, CreatedUtc, " +
            "FingerprintAlgorithm, FingerprintValue, MetadataJson " +
            "FROM Artifacts WHERE Id = $id;";

        command.Parameters.AddWithValue("$id", artifactId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var metadataJson = reader.GetString(6);

        return new Artifact
        {
            Id = new ArtifactId(reader.GetString(0)),
            Name = reader.GetString(1),
            ArtifactType = reader.GetString(2),
            CreatedUtc = DateTimeOffset.Parse(reader.GetString(3)),
            Fingerprint = reader.IsDBNull(4) || reader.IsDBNull(5)
                ? null
                : new EMF.Core.Models.Integrity.ContentFingerprint
                {
                    Algorithm = reader.GetString(4),
                    Value = reader.GetString(5)
                },
            Metadata =
                JsonSerializer.Deserialize<Dictionary<string, object>>(
                    metadataJson)
                ?? new Dictionary<string, object>()
        };
    }

    public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
