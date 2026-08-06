using EMF.Inventory.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Inventory.Providers;

public sealed class SqliteInventoryProvider
{
    public async Task<DatabaseInventory> CreateInventoryAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("SQLite database was not found.", databasePath);
        }

        var inventory = new DatabaseInventory
        {
            DatabasePath = Path.GetFullPath(databasePath),
            DatabaseEngine = "SQLite"
        };

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        inventory.DatabaseVersion = await GetDatabaseVersionAsync(
            connection,
            cancellationToken);

        var tableNames = await GetTableNamesAsync(
            connection,
            cancellationToken);

        foreach (var tableName in tableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var table = new TableInventory
            {
                Name = tableName,
                RowCount = await GetRowCountAsync(
                    connection,
                    tableName,
                    cancellationToken)
            };

            await LoadColumnsAsync(
                connection,
                table,
                cancellationToken);

            inventory.Tables.Add(table);
        }

        return inventory;
    }

    private static async Task<string> GetDatabaseVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString() ?? string.Empty;
    }

    private static async Task<List<string>> GetTableNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<long> GetRowCountAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var escapedTableName = QuoteIdentifier(tableName);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {escapedTableName};";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static async Task LoadColumnsAsync(
        SqliteConnection connection,
        TableInventory table,
        CancellationToken cancellationToken)
    {
        var escapedTableName = QuoteIdentifier(table.Name);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({escapedTableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var column = new ColumnInventory
            {
                Name = reader.GetString(1),
                DataType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                IsNullable = reader.GetInt64(3) == 0,
                DefaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                IsPrimaryKey = reader.GetInt64(5) > 0
            };

            table.Columns.Add(column);

            if (column.IsPrimaryKey)
            {
                table.PrimaryKeys.Add(column.Name);
            }
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
