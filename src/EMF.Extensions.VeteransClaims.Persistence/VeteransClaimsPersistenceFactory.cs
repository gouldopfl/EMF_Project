namespace EMF.Extensions.VeteransClaims.Persistence;

public static class VeteransClaimsPersistenceFactory
{
    public static IVeteransClaimsPersistence Create(
        VeteransClaimsPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.Provider);

        if (string.Equals(
            options.Provider,
            VeteransClaimsPersistenceProviders.Sqlite,
            StringComparison.OrdinalIgnoreCase))
        {
            return CreateSqlite(options.Settings);
        }

        throw new NotSupportedException(
            $"Veterans Claims persistence provider " +
            $"'{options.Provider}' is not supported.");
    }

    private static IVeteransClaimsPersistence CreateSqlite(
        IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var databasePath =
            settings
                .Where(
                    item => string.Equals(
                        item.Key,
                        VeteransClaimsPersistenceSettings
                            .DatabasePath,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Value)
                .SingleOrDefault();

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new InvalidOperationException(
                "SQLite Veterans Claims persistence " +
                "requires a DatabasePath setting.");
        }

        return new SqliteVeteransClaimsPersistence(
            databasePath);
    }
}
