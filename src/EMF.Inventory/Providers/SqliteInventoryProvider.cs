using System.Diagnostics;
using EMF.Inventory.Contracts;
using EMF.Inventory.Models;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace EMF.Inventory.Providers;

public sealed class SqliteInventoryProvider : IInventoryProvider
{
    private readonly InventoryProcessingLimits _limits;
    public SqliteInventoryProvider() : this(new InventoryProcessingLimits()) { }
    public SqliteInventoryProvider(InventoryProcessingLimits limits) { _limits = limits; _limits.Validate(); }
    public bool CanHandle(string sourcePath) => !string.IsNullOrWhiteSpace(sourcePath) &&
        Path.GetExtension(sourcePath).ToLowerInvariant() is ".db" or ".sqlite" or ".sqlite3";

    public async Task<DatabaseInventory> CreateInventoryAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!File.Exists(databasePath)) throw new FileNotFoundException("SQLite database was not found.", databasePath);
        if (new FileInfo(databasePath).Length > _limits.MaximumSnapshotBytes) throw new InvalidDataException("Inventory input exceeds byte admission limit.");
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 0 }.ToString());
        await c.OpenAsync(cancellationToken);
        long work = 0; var watch = Stopwatch.StartNew(); bool exhausted = false;
        raw.sqlite3_progress_handler(c.Handle!, 1000, _ =>
        {
            work = checked(work + 1000); exhausted = work > _limits.MaximumQueryInstructions || watch.Elapsed > _limits.ExecutionBudget;
            return cancellationToken.IsCancellationRequested || exhausted ? 1 : 0;
        }, null!);
        try
        {
            var budget = new SchemaBudget(_limits);
            var inventory = new DatabaseInventory { DatabasePath = Path.GetFullPath(databasePath), DatabaseEngine = "SQLite" };
            using (var version = c.CreateCommand()) { version.CommandText = "SELECT sqlite_version()"; inventory.DatabaseVersion = (await version.ExecuteScalarAsync(cancellationToken))?.ToString() ?? ""; }
            var names = new List<string>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT name,length(name) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                { if (names.Count >= _limits.MaximumTables) throw new InvalidDataException("Inventory table limit."); budget.Text(r.GetInt64(1)); names.Add(r.GetString(0)); }
            }
            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var table = new TableInventory { Name = name };
                using (var count = c.CreateCommand()) { count.CommandText = "SELECT COUNT(*) FROM \"" + name.Replace("\"", "\"\"") + "\""; table.RowCount = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken)); }
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT name,type,\"notnull\",dflt_value,pk,length(name),length(type),length(dflt_value) FROM pragma_table_info($name) ORDER BY cid";
                    cmd.Parameters.AddWithValue("$name", name); using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await r.ReadAsync(cancellationToken))
                    {
                        budget.Column(); for (int i = 5; i < 8; i++) if (!r.IsDBNull(i)) budget.Text(r.GetInt64(i));
                        var column = new ColumnInventory
                        {
                            Name = r.GetString(0),
                            DataType = r.IsDBNull(1) ? "" : r.GetString(1),
                            IsNullable = r.GetInt64(2) == 0,
                            DefaultValue = r.IsDBNull(3) ? null : r.GetString(3),
                            IsPrimaryKey = r.GetInt64(4) > 0
                        };
                        table.Columns.Add(column); if (column.IsPrimaryKey) table.PrimaryKeys.Add(column.Name);
                    }
                }
                inventory.Tables.Add(table);
            }
            return inventory;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == raw.SQLITE_INTERRUPT)
        { cancellationToken.ThrowIfCancellationRequested(); if (exhausted) throw new InvalidDataException("Inventory schema work budget exhausted.", e); throw; }
        finally { raw.sqlite3_progress_handler(c.Handle!, 0, null!, null!); }
    }
    private sealed class SchemaBudget(InventoryProcessingLimits limits)
    {
        private long _text; private int _columns;
        public void Column() { if (checked(++_columns) > limits.MaximumColumns) throw new InvalidDataException("Inventory column limit."); }
        public void Text(long length) { if (length < 0 || length > limits.MaximumStringCharacters || checked(_text += length) > limits.MaximumTextCharacters) throw new InvalidDataException("Inventory metadata text limit."); }
    }
}
