using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteClaimIssueCourtAppealRepository :
    IClaimIssueCourtAppealRepository
{
    private readonly string _databasePath;

    public SqliteClaimIssueCourtAppealRepository(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection() =>
        VeteransClaimsSqliteConnectionFactory.Create(_databasePath);

    public async Task AddAsync(
        ClaimIssueCourtAppeal appeal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appeal);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ClaimIssueCourtAppeals (
                ClaimIssueId,
                Court,
                FiledAt,
                DocketNumber,
                Outcome,
                DecidedAt
            )
            VALUES (
                $claimIssueId,
                $court,
                $filedAt,
                $docketNumber,
                $outcome,
                $decidedAt
            );
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            appeal.ClaimIssueId.Value);
        command.Parameters.AddWithValue(
            "$court",
            appeal.Court);
        command.Parameters.AddWithValue(
            "$filedAt",
            appeal.FiledAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$docketNumber",
            appeal.DocketNumber is null
                ? DBNull.Value
                : appeal.DocketNumber);
        command.Parameters.AddWithValue(
            "$outcome",
            appeal.Outcome is null
                ? DBNull.Value
                : appeal.Outcome);
        command.Parameters.AddWithValue(
            "$decidedAt",
            appeal.DecidedAt is null
                ? DBNull.Value
                : appeal.DecidedAt.Value.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimIssueCourtAppeal>>
        GetByClaimIssueAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Court,
                FiledAt,
                DocketNumber,
                Outcome,
                DecidedAt
            FROM VeteransClaims_ClaimIssueCourtAppeals
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY FiledAt;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var results = new List<ClaimIssueCourtAppeal>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new ClaimIssueCourtAppeal
                {
                    ClaimIssueId = claimIssueId,
                    Court = reader.GetString(0),
                    FiledAt =
                        DateTimeOffset.Parse(reader.GetString(1)),
                    DocketNumber =
                        reader.IsDBNull(2)
                            ? null
                            : reader.GetString(2),
                    Outcome =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetString(3),
                    DecidedAt =
                        reader.IsDBNull(4)
                            ? null
                            : DateTimeOffset.Parse(
                                reader.GetString(4))
                });
        }

        return results;
    }
}
