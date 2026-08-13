using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
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

            CREATE TABLE IF NOT EXISTS Provenance (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ArtifactId TEXT NOT NULL,
                Source TEXT NOT NULL,
                RecordedUtc TEXT NOT NULL,
                RecordedBy TEXT NOT NULL,
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

    public async Task AddRelationshipAsync(
        Relationship relationship,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Relationships " +
            "(SourceArtifactId, TargetArtifactId, RelationshipType, CreatedUtc, PropertiesJson) " +
            "VALUES ($sourceArtifactId, $targetArtifactId, $relationshipType, $createdUtc, $propertiesJson);";

        command.Parameters.AddWithValue(
            "$sourceArtifactId",
            relationship.SourceArtifactId.Value);
        command.Parameters.AddWithValue(
            "$targetArtifactId",
            relationship.TargetArtifactId.Value);
        command.Parameters.AddWithValue(
            "$relationshipType",
            relationship.RelationshipType);
        command.Parameters.AddWithValue(
            "$createdUtc",
            relationship.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$propertiesJson",
            JsonSerializer.Serialize(relationship.Properties));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

    public async Task<Artifact?> FindArtifactAsync(
        string source,
        ContentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.Id, a.Name, a.ArtifactType, a.CreatedUtc,
                   a.FingerprintAlgorithm, a.FingerprintValue, a.MetadataJson
            FROM Artifacts a
            INNER JOIN Provenance p ON p.ArtifactId = a.Id
            WHERE p.Source = $source
              AND a.FingerprintAlgorithm = $algorithm
              AND a.FingerprintValue = $value
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$algorithm", fingerprint.Algorithm);
        command.Parameters.AddWithValue("$value", fingerprint.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new Artifact
        {
            Id = new ArtifactId(reader.GetString(0)),
            Name = reader.GetString(1),
            ArtifactType = reader.GetString(2),
            CreatedUtc = DateTimeOffset.Parse(reader.GetString(3)),
            Fingerprint = new ContentFingerprint
            {
                Algorithm = reader.GetString(4),
                Value = reader.GetString(5)
            },
            Metadata =
                JsonSerializer.Deserialize<Dictionary<string, object>>(
                    reader.GetString(6))
                ?? new Dictionary<string, object>()
        };
    }

    public async Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT SourceArtifactId, TargetArtifactId, RelationshipType, CreatedUtc, PropertiesJson " +
            "FROM Relationships " +
            "WHERE SourceArtifactId = $artifactId OR TargetArtifactId = $artifactId " +
            "ORDER BY Id;";

        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        var relationships = new List<Relationship>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var propertiesJson = reader.GetString(4);

            relationships.Add(new Relationship
            {
                SourceArtifactId =
                    new ArtifactId(reader.GetString(0)),
                TargetArtifactId =
                    new ArtifactId(reader.GetString(1)),
                RelationshipType =
                    reader.GetString(2),
                CreatedUtc =
                    DateTimeOffset.Parse(reader.GetString(3)),
                Properties =
                    JsonSerializer.Deserialize<Dictionary<string, object>>(
                        propertiesJson)
                    ?? new Dictionary<string, object>()
            });
        }

        return relationships;
    }


    public async Task AddProvenanceAsync(
        Provenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Provenance (ArtifactId, Source, RecordedUtc, RecordedBy, PropertiesJson) VALUES ($artifactId, $source, $recordedUtc, $recordedBy, $propertiesJson);";

        command.Parameters.AddWithValue("$artifactId", provenance.ArtifactId.Value);
        command.Parameters.AddWithValue("$source", provenance.Source);
        command.Parameters.AddWithValue("$recordedUtc", provenance.RecordedUtc.ToString("O"));
        command.Parameters.AddWithValue("$recordedBy", provenance.RecordedBy);
        command.Parameters.AddWithValue("$propertiesJson", JsonSerializer.Serialize(provenance.Properties));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    public async Task AddArtifactWithProvenanceAsync(
        Artifact artifact,
        Provenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(provenance);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var artifactCommand = connection.CreateCommand())
            {
                artifactCommand.Transaction = (SqliteTransaction)transaction;
                artifactCommand.CommandText =
                    "INSERT OR REPLACE INTO Artifacts " +
                    "(Id, Name, ArtifactType, CreatedUtc, FingerprintAlgorithm, FingerprintValue, MetadataJson) " +
                    "VALUES ($id, $name, $artifactType, $createdUtc, $fingerprintAlgorithm, $fingerprintValue, $metadataJson);";

                artifactCommand.Parameters.AddWithValue("$id", artifact.Id.Value);
                artifactCommand.Parameters.AddWithValue("$name", artifact.Name);
                artifactCommand.Parameters.AddWithValue("$artifactType", artifact.ArtifactType);
                artifactCommand.Parameters.AddWithValue("$createdUtc", artifact.CreatedUtc.ToString("O"));
                artifactCommand.Parameters.AddWithValue(
                    "$fingerprintAlgorithm",
                    (object?)artifact.Fingerprint?.Algorithm ?? DBNull.Value);
                artifactCommand.Parameters.AddWithValue(
                    "$fingerprintValue",
                    (object?)artifact.Fingerprint?.Value ?? DBNull.Value);
                artifactCommand.Parameters.AddWithValue(
                    "$metadataJson",
                    JsonSerializer.Serialize(artifact.Metadata));

                await artifactCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var provenanceCommand = connection.CreateCommand())
            {
                provenanceCommand.Transaction = (SqliteTransaction)transaction;
                provenanceCommand.CommandText =
                    "INSERT INTO Provenance " +
                    "(ArtifactId, Source, RecordedUtc, RecordedBy, PropertiesJson) " +
                    "VALUES ($artifactId, $source, $recordedUtc, $recordedBy, $propertiesJson);";

                provenanceCommand.Parameters.AddWithValue(
                    "$artifactId",
                    provenance.ArtifactId.Value);
                provenanceCommand.Parameters.AddWithValue(
                    "$source",
                    provenance.Source);
                provenanceCommand.Parameters.AddWithValue(
                    "$recordedUtc",
                    provenance.RecordedUtc.ToString("O"));
                provenanceCommand.Parameters.AddWithValue(
                    "$recordedBy",
                    provenance.RecordedBy);
                provenanceCommand.Parameters.AddWithValue(
                    "$propertiesJson",
                    JsonSerializer.Serialize(provenance.Properties));

                await provenanceCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ArtifactId, Source, RecordedUtc, RecordedBy, PropertiesJson FROM Provenance WHERE ArtifactId = $artifactId ORDER BY Id;";
        command.Parameters.AddWithValue("$artifactId", artifactId.Value);

        var results = new List<Provenance>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var propertiesJson = reader.GetString(4);

            results.Add(new Provenance
            {
                ArtifactId = new ArtifactId(reader.GetString(0)),
                Source = reader.GetString(1),
                RecordedUtc = DateTimeOffset.Parse(reader.GetString(2)),
                RecordedBy = reader.GetString(3),
                Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(propertiesJson)
                    ?? new Dictionary<string, object>()
            });
        }

        return results;
    }

}
