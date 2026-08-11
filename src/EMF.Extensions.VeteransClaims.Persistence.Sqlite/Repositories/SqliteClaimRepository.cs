using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteClaimRepository : IClaimRepository
{
    private readonly string _databasePath;

    public SqliteClaimRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
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

    public async Task AddClaimAsync(
        Claim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Claims (
                Id,
                VeteranId
            )
            VALUES (
                $id,
                $veteranId
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            claim.Id.Value);

        command.Parameters.AddWithValue(
            "$veteranId",
            claim.VeteranId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Claim?> GetClaimAsync(
        ClaimId claimId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId
            FROM VeteransClaims_Claims
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateClaim(reader);
    }

    public async Task<IReadOnlyList<Claim>> GetClaimsAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId
            FROM VeteransClaims_Claims
            WHERE VeteranId = $veteranId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$veteranId",
            veteranId.Value);

        var claims = new List<Claim>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            claims.Add(CreateClaim(reader));
        }

        return claims;
    }

    private static Claim CreateClaim(
        SqliteDataReader reader)
    {
        return new Claim
        {
            Id = new ClaimId(reader.GetString(0)),
            VeteranId =
                new VeteranId(reader.GetString(1))
        };
    }
}
