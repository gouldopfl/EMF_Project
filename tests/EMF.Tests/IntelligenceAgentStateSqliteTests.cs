using EMF.Intelligence.Persistence.Sqlite;

namespace EMF.Tests;

public sealed class IntelligenceAgentStateSqliteTests
{
    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var store =
                new SqliteIntelligenceAgentStateStore(databasePath);

            await store.InitializeAsync();
            await store.InitializeAsync();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_RoundTripsState()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var store =
                new SqliteIntelligenceAgentStateStore(databasePath);

            await store.InitializeAsync();

            var state = new EMF.Intelligence.State.IntelligenceAgentState
            {
                AgentId =
                    new EMF.Intelligence.Models.Identities.AgentId(
                        "state-agent"),
                StateId = "session-001",
                Version = 1,
                Revision = 0,
                Payload = """{"step":1}""",
                UpdatedUtc =
                    new DateTimeOffset(
                        2026, 8, 21, 20, 0, 0,
                        TimeSpan.Zero)
            };

            await store.SaveAsync(state);

            var loaded =
                await store.GetAsync(
                    state.AgentId,
                    state.StateId);

            Assert.NotNull(loaded);
            Assert.Equal(state.AgentId, loaded.AgentId);
            Assert.Equal(state.StateId, loaded.StateId);
            Assert.Equal(state.Version, loaded.Version);
            Assert.Equal(1, loaded.Revision);
            Assert.Equal(state.Payload, loaded.Payload);
            Assert.Equal(state.UpdatedUtc, loaded.UpdatedUtc);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task SaveAsync_RejectsStaleRevision()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var store =
                new SqliteIntelligenceAgentStateStore(databasePath);

            await store.InitializeAsync();

            var state = new EMF.Intelligence.State.IntelligenceAgentState
            {
                AgentId = new("state-agent"),
                StateId = "session-stale",
                Version = 1,
                Revision = 1,
                Payload = "{}",
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            await store.SaveAsync(state);

            var updated = new EMF.Intelligence.State.IntelligenceAgentState
            {
                AgentId = state.AgentId,
                StateId = state.StateId,
                Version = state.Version,
                Revision = 1,
                Payload = """{"updated":true}""",
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            await store.SaveAsync(updated);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SaveAsync(state));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task InitializeAsync_UpgradesVersionOneStateSchema()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await using var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                CREATE TABLE IntelligenceAgentState_SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    AppliedUtc TEXT NOT NULL
                );

                INSERT INTO IntelligenceAgentState_SchemaMigrations
                VALUES (
                    1,
                    'InitialIntelligenceAgentStateSchema',
                    '2026-08-21T20:00:00+00:00'
                );

                CREATE TABLE IntelligenceAgentStates (
                    AgentId TEXT NOT NULL,
                    StateId TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    Payload TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    PRIMARY KEY (AgentId, StateId)
                );

                INSERT INTO IntelligenceAgentStates
                VALUES (
                    'state-agent',
                    'session-old',
                    1,
                    '{}',
                    '2026-08-21T20:00:00+00:00'
                );
                """;

            await command.ExecuteNonQueryAsync();

            var store =
                new SqliteIntelligenceAgentStateStore(
                    databasePath);

            await store.InitializeAsync();

            var loaded =
                await store.GetAsync(
                    new("state-agent"),
                    "session-old");

            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.Revision);

            await store.SaveAsync(loaded);

            var updated =
                await store.GetAsync(
                    loaded.AgentId,
                    loaded.StateId);

            Assert.NotNull(updated);
            Assert.Equal(1, updated.Revision);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_RejectsUnsupportedSchemaVersion()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await using var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                CREATE TABLE IntelligenceAgentState_SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    AppliedUtc TEXT NOT NULL
                );

                INSERT INTO IntelligenceAgentState_SchemaMigrations (
                    Version,
                    Name,
                    AppliedUtc
                )
                VALUES (
                    99,
                    'FutureSchema',
                    '2026-08-21T20:00:00.0000000+00:00'
                );
                """;

            await command.ExecuteNonQueryAsync();

            var store =
                new SqliteIntelligenceAgentStateStore(databasePath);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.InitializeAsync());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesOnlyOwnedTables()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var store =
                new SqliteIntelligenceAgentStateStore(databasePath);

            await store.InitializeAsync();

            await using var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;
                """;

            var tables = new List<string>();

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

            Assert.Equal(
                new[]
                {
                    "IntelligenceAgentState_SchemaMigrations",
                    "IntelligenceAgentStates"
                },
                tables);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
