namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

public sealed class VeteransClaimsSqliteSchema
{
    private readonly string _databasePath;

    public VeteransClaimsSqliteSchema(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var migrator =
            new VeteransClaimsSqliteMigrator(
                _databasePath,
                VeteransClaimsSqliteMigrations.All);

        return migrator.MigrateAsync(cancellationToken);
    }
}
