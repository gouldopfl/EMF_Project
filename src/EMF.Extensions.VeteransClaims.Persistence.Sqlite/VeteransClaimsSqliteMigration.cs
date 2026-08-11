namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

internal sealed record VeteransClaimsSqliteMigration
{
    public VeteransClaimsSqliteMigration(
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
