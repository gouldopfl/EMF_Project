using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.State;
using Microsoft.Data.Sqlite;

namespace EMF.Intelligence.Persistence.Sqlite;

public sealed class SqliteIntelligenceAgentStateStore :
    IIntelligenceAgentStateStore
{
    private readonly string _databasePath;

    public SqliteIntelligenceAgentStateStore(string databasePath)
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

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var migrator = new IntelligenceAgentStateSqliteMigrator(
            _databasePath,
            IntelligenceAgentStateSqliteMigrations.All);

        return migrator.MigrateAsync(cancellationToken);
    }

    public async Task<IntelligenceAgentState?> GetAsync(
        AgentId agentId,
        string stateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT AgentId, StateId, Version, Revision, Payload, UpdatedUtc
            FROM IntelligenceAgentStates
            WHERE AgentId = $agentId AND StateId = $stateId;
            """;

        command.Parameters.AddWithValue("$agentId", agentId.Value);
        command.Parameters.AddWithValue("$stateId", stateId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new IntelligenceAgentState
        {
            AgentId = new AgentId(reader.GetString(0)),
            StateId = reader.GetString(1),
            Version = reader.GetInt32(2),
            Revision = reader.GetInt32(3),
            Payload = reader.GetString(4),
            UpdatedUtc = DateTimeOffset.Parse(reader.GetString(5))
        };
    }

    public async Task SaveAsync(
        IntelligenceAgentState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.StateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Payload);

        if (state.Version < 1)
            throw new ArgumentOutOfRangeException(nameof(state));

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO IntelligenceAgentStates (
                AgentId,
                StateId,
                Version,
                Revision,
                Payload,
                UpdatedUtc
            )
            VALUES (
                $agentId,
                $stateId,
                $version,
                $revision,
                $payload,
                $updatedUtc
            )
            ON CONFLICT (AgentId, StateId)
            DO UPDATE SET
                Version = excluded.Version,
                Revision = excluded.Revision,
                Payload = excluded.Payload,
                UpdatedUtc = excluded.UpdatedUtc
            WHERE excluded.Revision >
                IntelligenceAgentStates.Revision;
            """;

        command.Parameters.AddWithValue("$agentId", state.AgentId.Value);
        command.Parameters.AddWithValue("$stateId", state.StateId);
        command.Parameters.AddWithValue("$version", state.Version);
        command.Parameters.AddWithValue("$revision", state.Revision);
        command.Parameters.AddWithValue("$payload", state.Payload);
        command.Parameters.AddWithValue(
            "$updatedUtc",
            state.UpdatedUtc.ToString("O"));

        var affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affected == 0)
        {
            throw new InvalidOperationException(
                "Intelligence agent state revision conflict.");
        }
    }
}
