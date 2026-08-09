using EMF.Core.Contracts;
using EMF.Core.Models.Workflow;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace EMF.Persistence.Repositories;

public sealed class SqliteWorkflowDefinitionRepository :
    IWorkflowDefinitionRepository
{
    private readonly string _databasePath;

    public SqliteWorkflowDefinitionRepository(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath
            };

        return new SqliteConnection(
            builder.ToString());
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS WorkflowDefinitions
            (
                DefinitionId TEXT NOT NULL,
                Version TEXT NOT NULL,
                Name TEXT NOT NULL,
                ActivityIds TEXT NOT NULL,
                PRIMARY KEY (DefinitionId, Version)
            );
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task StoreDefinitionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            definition);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO WorkflowDefinitions
            (
                DefinitionId,
                Version,
                Name,
                ActivityIds
            )
            VALUES
            (
                $definitionId,
                $version,
                $name,
                $activityIds
            );
            """;

        command.Parameters.AddWithValue(
            "$definitionId",
            definition.Id);

        command.Parameters.AddWithValue(
            "$version",
            definition.Version);

        command.Parameters.AddWithValue(
            "$name",
            definition.Name);

        command.Parameters.AddWithValue(
            "$activityIds",
            JsonSerializer.Serialize(
                definition.ActivityIds));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<WorkflowDefinition?> GetDefinitionAsync(
        string definitionId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            definitionId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            version);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT Name, ActivityIds
            FROM WorkflowDefinitions
            WHERE DefinitionId = $definitionId
              AND Version = $version;
            """;

        command.Parameters.AddWithValue(
            "$definitionId",
            definitionId);

        command.Parameters.AddWithValue(
            "$version",
            version);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        var activityIds =
            JsonSerializer.Deserialize<string[]>(
                reader.GetString(1))
            ?? Array.Empty<string>();

        return new WorkflowDefinition
        {
            Id = definitionId,
            Name = reader.GetString(0),
            Version = version,
            ActivityIds = activityIds
        };
    }
}
