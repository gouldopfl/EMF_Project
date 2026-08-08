using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;

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

    public Task AddArtifactAsync(
        Artifact artifact,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task AddRelationshipAsync(
        Relationship relationship,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<Artifact?> GetArtifactAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
