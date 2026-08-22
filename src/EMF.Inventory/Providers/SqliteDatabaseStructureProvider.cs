using EMF.Inventory.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Inventory.Providers;

public sealed class SqliteDatabaseStructureProvider
{
    public async Task<DatabaseStructure> DiscoverAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException(
                "SQLite database was not found.",
                databasePath);

        var result = new DatabaseStructure
        {
            DatabaseEngine = "SQLite"
        };

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

        await using var connection =
            new SqliteConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        await using (var versionCommand =
            connection.CreateCommand())
        {
            versionCommand.CommandText =
                "SELECT sqlite_version();";

            result.DatabaseVersion =
                (await versionCommand.ExecuteScalarAsync(
                    cancellationToken))?.ToString()
                ?? string.Empty;
        }

        result.Schemas.Add(
            new DatabaseSchema { Name = "main" });

        var schema = result.Schemas[0];

        await LoadTablesAsync(
            connection,
            schema,
            cancellationToken);

        await LoadViewsAsync(
            connection,
            result,
            cancellationToken);

        return result;
    }

    private static async Task LoadTablesAsync(
        SqliteConnection connection,
        DatabaseSchema schema,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var table = new DatabaseTable
            {
                Name = reader.GetString(0),
                RowCount = await GetRowCountAsync(
                    connection,
                    reader.GetString(0),
                    cancellationToken)
            };

            await LoadColumnsAsync(
                connection,
                table,
                cancellationToken);

            await LoadForeignKeysAsync(
                connection,
                table,
                cancellationToken);

            await LoadIndexesAsync(
                connection,
                table,
                cancellationToken);

            schema.Tables.Add(table);
        }
    }

    private static async Task LoadViewsAsync(
        SqliteConnection connection,
        DatabaseStructure structure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master " +
            "WHERE type = 'view' ORDER BY name;";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            structure.Views.Add(
                new DatabaseView
                {
                    Name = reader.GetString(0)
                });
        }
    }

    private static async Task LoadIndexesAsync(
        SqliteConnection connection,
        DatabaseTable table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA index_list(\"{table.Name.Replace("\"", "\"\"")}\");";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var index =
                new DatabaseIndex
                {
                    Name = reader.GetString(1),
                    IsUnique = reader.GetInt64(2) != 0
                };

            await LoadIndexColumnsAsync(
                connection,
                index,
                cancellationToken);

            table.Indexes.Add(index);
        }
    }

    private static async Task LoadIndexColumnsAsync(
        SqliteConnection connection,
        DatabaseIndex index,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA index_info(\"{index.Name.Replace("\"", "\"\"")}\");";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(2))
                index.Columns.Add(reader.GetString(2));
        }
    }

    private static async Task LoadForeignKeysAsync(
        SqliteConnection connection,
        DatabaseTable table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA foreign_key_list(\"{table.Name.Replace("\"", "\"\"")}\");";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            table.ForeignKeys.Add(
                new DatabaseForeignKey
                {
                    ColumnName = reader.GetString(3),
                    ReferencedTable = reader.GetString(2),
                    ReferencedColumn = reader.GetString(4)
                });
        }
    }

    private static async Task<long> GetRowCountAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\";";

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt64(result);
    }

    private static async Task LoadColumnsAsync(
        SqliteConnection connection,
        DatabaseTable table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA table_info(\"{table.Name.Replace("\"", "\"\"")}\");";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            table.Columns.Add(
                new DatabaseColumn
                {
                    Name = reader.GetString(1),
                    DataType = reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2),
                    IsNullable = reader.GetInt64(3) == 0,
                    DefaultValue = reader.IsDBNull(4)
                        ? null
                        : reader.GetValue(4).ToString(),
                    IsPrimaryKey = reader.GetInt64(5) > 0
                });
        }
    }

}
