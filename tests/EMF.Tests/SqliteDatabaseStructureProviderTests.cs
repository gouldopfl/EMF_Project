using EMF.Inventory.Providers;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SqliteDatabaseStructureProviderTests
{
    [Fact]
    public async Task DiscoverAsync_ReadsViews()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-structure-{Guid.NewGuid():N}.db");

        try
        {
            await using var connection =
                new SqliteConnection($"Data Source={path}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE evidence (id INTEGER PRIMARY KEY, name TEXT); " +
                "CREATE VIEW evidence_view AS SELECT id, name FROM evidence;";

            await command.ExecuteNonQueryAsync();

            var structure =
                await new SqliteDatabaseStructureProvider()
                    .DiscoverAsync(path);

            var view = Assert.Single(structure.Views);

            Assert.Equal("evidence_view", view.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DiscoverAsync_ReadsIndexes()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-structure-{Guid.NewGuid():N}.db");

        try
        {
            await using var connection =
                new SqliteConnection($"Data Source={path}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE evidence (
                    id INTEGER PRIMARY KEY,
                    veteran_id INTEGER,
                    status TEXT
                );

                CREATE UNIQUE INDEX ix_evidence_veteran
                    ON evidence(veteran_id);
                """;

            await command.ExecuteNonQueryAsync();

            var structure =
                await new SqliteDatabaseStructureProvider()
                    .DiscoverAsync(path);

            var schema = Assert.Single(structure.Schemas);
            var table = Assert.Single(
                schema.Tables.Where(
                    t => t.Name == "evidence"));

            var index =
                Assert.Single(table.Indexes);

            Assert.Equal(
                "ix_evidence_veteran",
                index.Name);

            Assert.True(index.IsUnique);
            Assert.Contains(
                "veteran_id",
                index.Columns);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DiscoverAsync_ReadsForeignKeys()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-structure-{Guid.NewGuid():N}.db");

        try
        {
            await using var connection =
                new SqliteConnection($"Data Source={path}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE veteran (
                    id INTEGER PRIMARY KEY
                );

                CREATE TABLE evidence (
                    id INTEGER PRIMARY KEY,
                    veteran_id INTEGER,
                    FOREIGN KEY (veteran_id)
                        REFERENCES veteran(id)
                );
                """;

            await command.ExecuteNonQueryAsync();

            var structure =
                await new SqliteDatabaseStructureProvider()
                    .DiscoverAsync(path);

            var schema = Assert.Single(structure.Schemas);
            var table = Assert.Single(
                schema.Tables.Where(
                    t => t.Name == "evidence"));

            var foreignKey =
                Assert.Single(table.ForeignKeys);

            Assert.Equal("veteran_id", foreignKey.ColumnName);
            Assert.Equal("veteran", foreignKey.ReferencedTable);
            Assert.Equal("id", foreignKey.ReferencedColumn);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DiscoverAsync_ReadsDatabaseStructure()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-structure-{Guid.NewGuid():N}.db");

        try
        {
            await using var connection =
                new SqliteConnection($"Data Source={path}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                CREATE TABLE evidence (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    status TEXT DEFAULT 'new'
                );
                """;

            await command.ExecuteNonQueryAsync();

            var provider =
                new SqliteDatabaseStructureProvider();

            var structure =
                await provider.DiscoverAsync(path);

            Assert.Equal("SQLite", structure.DatabaseEngine);
            Assert.False(string.IsNullOrWhiteSpace(
                structure.DatabaseVersion));

            var schema =
                Assert.Single(structure.Schemas);

            Assert.Equal("main", schema.Name);

            var table =
                Assert.Single(schema.Tables);

            Assert.Equal("evidence", table.Name);
            Assert.Equal(0, table.RowCount);
            Assert.Equal(3, table.Columns.Count);

            var id =
                Assert.Single(
                    table.Columns.Where(
                        c => c.Name == "id"));

            Assert.Equal("INTEGER", id.DataType);
            Assert.True(id.IsPrimaryKey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
