using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Storage;

internal static class ContentCatalogSchema
{
    internal const int Version = 3;
    private const string StateSql = "CREATE TABLE ContentState(ArtifactId TEXT PRIMARY KEY,Revision TEXT NOT NULL,Generation TEXT,Length INTEGER NOT NULL,Owner TEXT)";
    private const string ReceiptsSql = "CREATE TABLE ContentReceipts(EnumerationSequence INTEGER PRIMARY KEY AUTOINCREMENT,OperationId TEXT NOT NULL UNIQUE,Request TEXT NOT NULL,Receipt TEXT NOT NULL,AuditEventId TEXT,PublishedGeneration TEXT,MutationRevision TEXT UNIQUE)";
    internal const string Foundation = StateSql + ";" + ReceiptsSql + "; PRAGMA user_version=2;";

    private static readonly (string Name, string Sql)[] LineageTriggers =
    [
        ("ContentReceiptOriginInsert", "CREATE TRIGGER ContentReceiptOriginInsert BEFORE INSERT ON ContentReceipts WHEN NEW.MutationRevision IS NOT NULL AND EXISTS(SELECT 1 FROM ContentMigrationArtifacts WHERE Revision=NEW.MutationRevision) BEGIN SELECT RAISE(ABORT,'Content revision already has migration authority'); END"),
        ("ContentReceiptOriginUpdate", "CREATE TRIGGER ContentReceiptOriginUpdate BEFORE UPDATE OF MutationRevision ON ContentReceipts WHEN NEW.MutationRevision IS NOT NULL AND EXISTS(SELECT 1 FROM ContentMigrationArtifacts WHERE Revision=NEW.MutationRevision) BEGIN SELECT RAISE(ABORT,'Content revision already has migration authority'); END"),
        ("ContentOriginReceiptInsert", "CREATE TRIGGER ContentOriginReceiptInsert BEFORE INSERT ON ContentMigrationArtifacts WHEN EXISTS(SELECT 1 FROM ContentReceipts WHERE MutationRevision=NEW.Revision) BEGIN SELECT RAISE(ABORT,'Content revision already has receipt authority'); END"),
        ("ContentOriginReceiptUpdate", "CREATE TRIGGER ContentOriginReceiptUpdate BEFORE UPDATE OF Revision ON ContentMigrationArtifacts WHEN EXISTS(SELECT 1 FROM ContentReceipts WHERE MutationRevision=NEW.Revision) BEGIN SELECT RAISE(ABORT,'Content revision already has receipt authority'); END")
    ];

    internal static void Upgrade(SqliteConnection connection, Action<SqliteConnection, SqliteTransaction>? validateFoundation = null)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(command.ExecuteScalar());
        if (version == Version) { Validate(connection, transaction); transaction.Commit(); return; }
        if (version != 2) throw new InvalidDataException("Content catalog schema version is unsupported.");
        command.CommandText = "PRAGMA quick_check";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Content catalog integrity check failed before schema upgrade.");
        ValidateFoundation(connection, transaction);
        RequireTriggerSet(connection, transaction, []);
        if (validateFoundation is not null) validateFoundation(connection, transaction);
        else
        {
            command.CommandText = "SELECT (SELECT count(*) FROM ContentState)+(SELECT count(*) FROM ContentReceipts)";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw new InvalidDataException("Existing content requires semantic lineage validation before schema upgrade.");
        }
        command.CommandText = """
            CREATE TABLE ContentSchemaMigrations(Version INTEGER PRIMARY KEY,Name TEXT NOT NULL,AppliedUtc TEXT NOT NULL);
            INSERT INTO ContentSchemaMigrations VALUES(2,'versioned-content-foundation',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            CREATE TABLE ContentMigrationRun(Id INTEGER PRIMARY KEY CHECK(Id=1),MigrationId TEXT NOT NULL,Root TEXT NOT NULL,Retention TEXT NOT NULL,Phase TEXT NOT NULL,ArtifactCount INTEGER NOT NULL,OfflineAcknowledged INTEGER NOT NULL CHECK(OfflineAcknowledged=1));
            CREATE TABLE ContentMigrationArtifacts(ArtifactId TEXT PRIMARY KEY,Length INTEGER NOT NULL,SourceStamp TEXT NOT NULL,Digest TEXT NOT NULL,Generation TEXT NOT NULL UNIQUE,Revision TEXT NOT NULL UNIQUE,Status TEXT NOT NULL);
            INSERT INTO ContentSchemaMigrations VALUES(3,'offline-legacy-migration-provenance',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            PRAGMA user_version=3;
            """;
        command.ExecuteNonQuery();
        foreach (var trigger in LineageTriggers)
        {
            command.CommandText = trigger.Sql;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    internal static void Validate(SqliteConnection connection) => Validate(connection, null);

    private static void Validate(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) != Version)
            throw new InvalidDataException("Content catalog schema version is unsupported.");
        ValidateFoundation(connection, transaction);
        RequireTriggerSet(connection, transaction, LineageTriggers.Select(trigger => trigger.Name));
        foreach (var trigger in LineageTriggers)
            RequireSql(connection, transaction, "trigger", trigger.Name, trigger.Sql);
        command.CommandText = "SELECT Version,Name FROM ContentSchemaMigrations ORDER BY Version";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != 2 || reader.GetString(1) != "versioned-content-foundation" ||
            !reader.Read() || reader.GetInt32(0) != 3 || reader.GetString(1) != "offline-legacy-migration-provenance" || reader.Read())
            throw new InvalidDataException("Content catalog migration ledger is damaged.");
    }

    private readonly record struct Column(string Name, string Type, bool NotNull, int PrimaryKey);
    private static void ValidateFoundation(SqliteConnection connection, SqliteTransaction? transaction)
    {
        RequireTable(connection, transaction, "ContentState", StateSql,
            [new("ArtifactId", "TEXT", false, 1), new("Revision", "TEXT", true, 0), new("Generation", "TEXT", false, 0),
             new("Length", "INTEGER", true, 0), new("Owner", "TEXT", false, 0)], [("ArtifactId", "pk")]);
        RequireTable(connection, transaction, "ContentReceipts", ReceiptsSql,
            [new("EnumerationSequence", "INTEGER", false, 1), new("OperationId", "TEXT", true, 0), new("Request", "TEXT", true, 0),
             new("Receipt", "TEXT", true, 0), new("AuditEventId", "TEXT", false, 0), new("PublishedGeneration", "TEXT", false, 0),
             new("MutationRevision", "TEXT", false, 0)], [("OperationId", "u"), ("MutationRevision", "u")]);
    }

    private static void RequireTable(SqliteConnection connection, SqliteTransaction? transaction, string table,
        string sql, Column[] columns, (string Column, string Origin)[] uniqueIndexes)
    {
        RequireSql(connection, transaction, "table", table, sql);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT cid,name,type,\"notnull\",dflt_value,pk,hidden FROM pragma_table_xinfo($table) ORDER BY cid";
        command.Parameters.AddWithValue("$table", table);
        using (var reader = command.ExecuteReader())
        {
            foreach (var column in columns)
                if (!reader.Read() || reader.GetInt32(0) != Array.IndexOf(columns, column) || reader.GetString(1) != column.Name ||
                    reader.GetString(2) != column.Type || (reader.GetInt32(3) != 0) != column.NotNull || !reader.IsDBNull(4) ||
                    reader.GetInt32(5) != column.PrimaryKey || reader.GetInt32(6) != 0)
                    throw new InvalidDataException("Content foundation column definition is unsupported.");
            if (reader.Read()) throw new InvalidDataException("Content foundation has unexpected columns.");
        }
        var indexes = new List<(string Name, string Origin)>();
        command.CommandText = "SELECT name,\"unique\",origin,partial FROM pragma_index_list($table)";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                if (reader.GetInt32(1) != 1 || reader.GetInt32(3) != 0)
                    throw new InvalidDataException("Content foundation index definition is unsupported.");
                indexes.Add((reader.GetString(0), reader.GetString(2)));
            }
        var actual = new HashSet<(string Column, string Origin)>();
        foreach (var index in indexes)
        {
            command.CommandText = "SELECT name,desc,coll FROM pragma_index_xinfo($index) WHERE key=1 ORDER BY seqno";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$index", index.Name);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0) || reader.GetInt32(1) != 0 || reader.GetString(2) != "BINARY" ||
                !actual.Add((reader.GetString(0), index.Origin)) || reader.Read())
                throw new InvalidDataException("Content foundation uniqueness definition is unsupported.");
        }
        if (!actual.SetEquals(uniqueIndexes)) throw new InvalidDataException("Content foundation uniqueness constraints are missing.");
    }

    private static void RequireTriggerSet(SqliteConnection connection, SqliteTransaction? transaction, IEnumerable<string> expected)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name IN ('ContentState','ContentReceipts','ContentMigrationArtifacts')";
        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) names.Add(reader.GetString(0));
        if (!names.SetEquals(expected)) throw new InvalidDataException("Content catalog has unexpected triggers.");
    }

    private static void RequireSql(SqliteConnection connection, SqliteTransaction? transaction, string type, string name, string expected)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type=$type AND name=$name";
        command.Parameters.AddWithValue("$type", type); command.Parameters.AddWithValue("$name", name);
        if (command.ExecuteScalar() is not string sql || NormalizeSql(sql) != NormalizeSql(expected))
            throw new InvalidDataException("Content catalog schema definition is unsupported.");
    }

    private static string NormalizeSql(string sql) => Regex.Replace(sql.Trim().TrimEnd(';'), @"\s+", "").ToUpperInvariant();
}
