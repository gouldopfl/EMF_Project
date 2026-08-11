using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

public sealed class VeteransClaimsSqliteSchema
{
    private readonly string _databasePath;

    public VeteransClaimsSqliteSchema(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        };

        await using var connection =
            new SqliteConnection(builder.ToString());

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS VeteransClaims_Veterans (
                Id TEXT PRIMARY KEY
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
