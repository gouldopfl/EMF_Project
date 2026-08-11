using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteConditionRepository
{
    private readonly string _databasePath;

    public SqliteConditionRepository(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

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
        return new VeteransClaimsSqliteSchema(
            _databasePath)
            .InitializeAsync(cancellationToken);
    }

    public async Task AddClaimedConditionAsync(
        ClaimedCondition claimedCondition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            claimedCondition);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ClaimedConditions (
                Id,
                ClaimIssueId,
                Name
            )
            VALUES (
                $id,
                $claimIssueId,
                $name
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimedCondition.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimedCondition.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$name",
            claimedCondition.Name);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
    public async Task<ClaimedCondition?>
        GetClaimedConditionAsync(
            ClaimedConditionId claimedConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Name
            FROM VeteransClaims_ClaimedConditions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimedConditionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedCondition
        {
            Id =
                new ClaimedConditionId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            Name = reader.GetString(2)
        };
    }


    public async Task<IReadOnlyList<ClaimedCondition>>
        GetClaimedConditionsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Name
            FROM VeteransClaims_ClaimedConditions
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var conditions = new List<ClaimedCondition>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            conditions.Add(
                new ClaimedCondition
                {
                    Id =
                        new ClaimedConditionId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    Name = reader.GetString(2)
                });
        }

        return conditions;
    }

}
