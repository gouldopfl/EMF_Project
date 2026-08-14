namespace EMF.Security.Persistence.Sqlite;

public sealed class SecurityAuditSqliteSchema
{
    private readonly string _databasePath;

    public SecurityAuditSqliteSchema(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var migrator =
            new SecurityAuditSqliteMigrator(
                _databasePath,
                SecurityAuditSqliteMigrations.All);

        return migrator.MigrateAsync(
            cancellationToken);
    }
}
