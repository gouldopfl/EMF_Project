using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Intelligence.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SqliteMigrationConcurrencyHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentInitializers_ApplyEachMigrationOnce(bool intelligence)
    {
        var path = Path.GetTempFileName();
        try
        {
            using var start = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                start.Wait();
                if (intelligence)
                    await new SqliteIntelligenceAgentStateStore(path).InitializeAsync();
                else
                    await new VeteransClaimsSqliteMigrator(path,
                    [
                        new(1, "Seed", "CREATE TABLE Marker(Value INTEGER); INSERT INTO Marker VALUES (0);"),
                        new(2, "Advance", "UPDATE Marker SET Value = Value + 1;")
                    ]).MigrateAsync();
            })).ToArray();
            start.Set();
            await Task.WhenAll(tasks);
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = intelligence
                ? "SELECT COUNT(*) FROM IntelligenceAgentState_SchemaMigrations;"
                : "SELECT Value FROM Marker;";
            Assert.Equal(intelligence ? 2L : 1L, await command.ExecuteScalarAsync());
        }
        finally { File.Delete(path); }
    }
}
