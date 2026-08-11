using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteClaimIssueRepository :
    IClaimIssueRepository
{
    private readonly string _databasePath;

    public SqliteClaimIssueRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        return VeteransClaimsSqliteConnectionFactory
            .Create(_databasePath);
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var schema =
            new VeteransClaimsSqliteSchema(_databasePath);

        return schema.InitializeAsync(cancellationToken);
    }

    public async Task AddClaimIssueAsync(
        ClaimIssue claimIssue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimIssue);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ClaimIssues (
                Id,
                ClaimId,
                ClaimIssueType
            )
            VALUES (
                $id,
                $claimId,
                $claimIssueType
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimIssue.Id.Value);

        command.Parameters.AddWithValue(
            "$claimId",
            claimIssue.ClaimId.Value);

        command.Parameters.AddWithValue(
            "$claimIssueType",
            claimIssue.ClaimIssueType);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ClaimIssue?> GetClaimIssueAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimId, ClaimIssueType
            FROM VeteransClaims_ClaimIssues
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimIssueId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateClaimIssue(reader);
    }

    public async Task<IReadOnlyList<ClaimIssue>>
        GetClaimIssuesAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimId, ClaimIssueType
            FROM VeteransClaims_ClaimIssues
            WHERE ClaimId = $claimId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimId",
            claimId.Value);

        var claimIssues = new List<ClaimIssue>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            claimIssues.Add(CreateClaimIssue(reader));
        }

        return claimIssues;
    }

    private static ClaimIssue CreateClaimIssue(
        SqliteDataReader reader)
    {
        return new ClaimIssue
        {
            Id =
                new ClaimIssueId(reader.GetString(0)),
            ClaimId =
                new ClaimId(reader.GetString(1)),
            ClaimIssueType =
                reader.GetString(2)
        };
    }
}
