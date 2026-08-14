namespace EMF.Security.Persistence.Sqlite;

internal sealed record SecurityAuditSqliteMigration
{
    public SecurityAuditSqliteMigration(
        int version,
        string name,
        string sql)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "Migration version must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        Version = version;
        Name = name;
        Sql = sql;
    }

    public int Version { get; }

    public string Name { get; }

    public string Sql { get; }
}
