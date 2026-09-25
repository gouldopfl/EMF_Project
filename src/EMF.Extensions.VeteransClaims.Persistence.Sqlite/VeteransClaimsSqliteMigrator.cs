using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

internal sealed class VeteransClaimsSqliteMigrator
{
    private readonly string _databasePath;
    private readonly IReadOnlyList<
        VeteransClaimsSqliteMigration> _migrations;

    public VeteransClaimsSqliteMigrator(
        string databasePath,
        IReadOnlyCollection<
            VeteransClaimsSqliteMigration> migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        ArgumentNullException.ThrowIfNull(migrations);

        _databasePath = databasePath;
        _migrations =
            migrations
                .OrderBy(migration => migration.Version)
                .ToArray();

        ValidateMigrations(_migrations);
    }

    public async Task InitializeLedgerAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            VeteransClaimsSqliteConnectionFactory
                .Create(_databasePath);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS
                VeteransClaims_SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    AppliedUtc TEXT NOT NULL
                );
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static void ValidateMigrations(
        IReadOnlyCollection<
            VeteransClaimsSqliteMigration> migrations)
    {
        var expectedVersion = 1;

        foreach (var migration in migrations)
        {
            if (migration.Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Veterans Claims migrations must form " +
                    "a contiguous sequence beginning with " +
                    "version 1.");
            }

            expectedVersion++;
        }

        var duplicateVersion =
            migrations
                .GroupBy(migration => migration.Version)
                .FirstOrDefault(group => group.Count() > 1);

        if (duplicateVersion is not null)
        {
            throw new InvalidOperationException(
                "Veterans Claims migrations contain " +
                $"duplicate version {duplicateVersion.Key}.");
        }
    }

    public async Task MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeLedgerAsync(cancellationToken);

        await using var connection =
            VeteransClaimsSqliteConnectionFactory
                .Create(_databasePath);

        await connection.OpenAsync(cancellationToken);

        var appliedMigrations =
            await GetAppliedMigrationsAsync(
                connection,
                cancellationToken);

        ValidateCompatibility(appliedMigrations);

        foreach (var migration in _migrations)
        {
            if (appliedMigrations.ContainsKey(
                migration.Version))
            {
                continue;
            }

            await ApplyMigrationAsync(
                connection,
                migration,
                cancellationToken);
        }
    }

    private static async Task<
        IReadOnlyDictionary<int, string>>
        GetAppliedMigrationsAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken,
            SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Version, Name
            FROM VeteransClaims_SchemaMigrations
            ORDER BY Version;
            """;

        var appliedMigrations =
            new Dictionary<int, string>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            appliedMigrations.Add(
                reader.GetInt32(0),
                reader.GetString(1));
        }

        return appliedMigrations;
    }

    private void ValidateCompatibility(
        IReadOnlyDictionary<int, string>
            appliedMigrations)
    {
        var supportedVersions =
            _migrations.ToDictionary(
                migration => migration.Version);

        // A valid ledger is a prefix of the supported history. Filling a hole
        // can replay destructive data migrations against a newer schema.
        if (!appliedMigrations.Keys.OrderBy(version => version)
                .SequenceEqual(Enumerable.Range(1, appliedMigrations.Count)))
        {
            throw new InvalidOperationException(
                "The database migration ledger is incomplete or noncontiguous.");
        }

        foreach (var appliedMigration in appliedMigrations)
        {
            if (!supportedVersions.TryGetValue(
                appliedMigration.Key,
                out var supportedMigration))
            {
                throw new InvalidOperationException(
                    "The Veterans Claims database contains " +
                    $"unsupported migration version " +
                    $"{appliedMigration.Key}.");
            }

            if (!string.Equals(
                appliedMigration.Value,
                supportedMigration.Name,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Veterans Claims database migration " +
                    $"{appliedMigration.Key} does not match " +
                    "the migration supported by this adapter.");
            }
        }
    }

    private async Task ApplyMigrationAsync(
        SqliteConnection connection,
        VeteransClaimsSqliteMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        // Read again under the write transaction: another initializer may have
        // advanced the ledger after our initial compatibility check.
        var applied = await GetAppliedMigrationsAsync(connection, cancellationToken, transaction);
        ValidateCompatibility(applied);
        if (applied.ContainsKey(migration.Version))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await using var migrationCommand =
            connection.CreateCommand();

        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText = migration.Sql;

        await migrationCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await using var ledgerCommand =
            connection.CreateCommand();

        ledgerCommand.Transaction = transaction;
        ledgerCommand.CommandText =
            """
            INSERT INTO VeteransClaims_SchemaMigrations (
                Version,
                Name,
                AppliedUtc
            )
            VALUES (
                $version,
                $name,
                $appliedUtc
            );
            """;

        ledgerCommand.Parameters.AddWithValue(
            "$version",
            migration.Version);

        ledgerCommand.Parameters.AddWithValue(
            "$name",
            migration.Name);

        ledgerCommand.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O"));

        await ledgerCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
