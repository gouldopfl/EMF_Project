using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteVeteranRepository : IVeteranRepository
{
    private readonly string _databasePath;

    public SqliteVeteranRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        };

        return new SqliteConnection(builder.ToString());
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var schema =
            new VeteransClaimsSqliteSchema(_databasePath);

        return schema.InitializeAsync(cancellationToken);
    }

    public async Task AddVeteranAsync(
        Veteran veteran,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(veteran);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Veterans (Id)
            VALUES ($id);
            """;

        command.Parameters.AddWithValue(
            "$id",
            veteran.Id.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Veteran?> GetVeteranAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id
            FROM VeteransClaims_Veterans
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            veteranId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Veteran
        {
            Id = new VeteranId(reader.GetString(0))
        };
    }
}
