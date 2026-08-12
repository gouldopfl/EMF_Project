using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteServiceConnectionRepository
{
    private readonly string _databasePath;

    public SqliteServiceConnectionRepository(
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

    public async Task AddServiceConnectionTheoryAsync(
        ServiceConnectionTheory theory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theory);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO
                VeteransClaims_ServiceConnectionTheories (
                    Id,
                    ClaimIssueId,
                    TheoryType
                )
            VALUES (
                $id,
                $claimIssueId,
                $theoryType
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            theory.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            theory.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$theoryType",
            theory.TheoryType);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
    public async Task<ServiceConnectionTheory?>
        GetServiceConnectionTheoryAsync(
            ServiceConnectionTheoryId theoryId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, TheoryType
            FROM VeteransClaims_ServiceConnectionTheories
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            theoryId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServiceConnectionTheory
        {
            Id =
                new ServiceConnectionTheoryId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            TheoryType = reader.GetString(2)
        };
    }

    public async Task<IReadOnlyList<ServiceConnectionTheory>>
        GetServiceConnectionTheoriesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, TheoryType
            FROM VeteransClaims_ServiceConnectionTheories
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var theories =
            new List<ServiceConnectionTheory>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            theories.Add(
                new ServiceConnectionTheory
                {
                    Id =
                        new ServiceConnectionTheoryId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    TheoryType = reader.GetString(2)
                });
        }

        return theories;
    }

}
