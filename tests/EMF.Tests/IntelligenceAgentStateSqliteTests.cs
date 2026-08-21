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
            Assert.Equal(state.Payload, loaded.Payload);
            Assert.Equal(state.UpdatedUtc, loaded.UpdatedUtc);
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
}
