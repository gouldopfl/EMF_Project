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

    [Fact]
    public async Task CreateInventoryAsync_CountsTableRows()
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

                    INSERT INTO evidence (name) VALUES ('one');
                    INSERT INTO evidence (name) VALUES ('two');
                    INSERT INTO evidence (name) VALUES ('three');
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var provider = new SqliteInventoryProvider();

            var inventory = await provider.CreateInventoryAsync(databasePath);

            var table = Assert.Single(inventory.Tables);

            Assert.Equal("evidence", table.Name);
            Assert.Equal(3, table.RowCount);
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
    public async Task CreateInventoryAsync_ReadsColumnMetadata()
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
                        name TEXT NOT NULL,
                        status TEXT DEFAULT 'new'
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var provider = new SqliteInventoryProvider();

            var inventory = await provider.CreateInventoryAsync(databasePath);

            var table = Assert.Single(inventory.Tables);

            var id = Assert.Single(
                table.Columns.Where(column => column.Name == "id"));

            var nameColumn = Assert.Single(
                table.Columns.Where(column => column.Name == "name"));

            var status = Assert.Single(
                table.Columns.Where(column => column.Name == "status"));

            Assert.Equal("INTEGER", id.DataType);
            Assert.True(id.IsPrimaryKey);

            Assert.Equal("TEXT", nameColumn.DataType);
            Assert.False(nameColumn.IsNullable);

            Assert.Equal("TEXT", status.DataType);
            Assert.True(status.IsNullable);
            Assert.Equal("'new'", status.DefaultValue);
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
    public async Task CreateInventoryAsync_WhenDatabaseDoesNotExist_ThrowsFileNotFoundException()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-missing-{Guid.NewGuid():N}.db");

        var provider = new SqliteInventoryProvider();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => provider.CreateInventoryAsync(databasePath));
    }

}
