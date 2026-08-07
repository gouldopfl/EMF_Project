using EMF.Inventory.Providers;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class InventoryTests
{
    [Fact]
    public async Task CreateInventoryAsync_ReadsSqliteSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-inventory-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString();

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE evidence (
                        id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var provider = new SqliteInventoryProvider();

            var inventory = await provider.CreateInventoryAsync(databasePath);

            Assert.Equal("SQLite", inventory.DatabaseEngine);
            Assert.Single(inventory.Tables);

            var table = inventory.Tables[0];

            Assert.Equal("evidence", table.Name);
            Assert.Equal(2, table.Columns.Count);
            Assert.Contains("id", table.PrimaryKeys);
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
