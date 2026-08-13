using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteFindingRepository :
    IFindingRepository
{
    private readonly string _databasePath;

    public SqliteFindingRepository(string databasePath)
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
        return new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);
    }

    public async Task AddFindingAsync(
        Finding finding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finding);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Findings (
                Id,
                ClaimIssueId,
                RequirementId,
                Outcome,
                Description
            )
            VALUES (
                $id,
                $claimIssueId,
                $requirementId,
                $outcome,
                $description
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            finding.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            finding.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$requirementId",
            finding.RequirementId.HasValue
                ? finding.RequirementId.Value.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$outcome",
            finding.Outcome);

        command.Parameters.AddWithValue(
            "$description",
            finding.Description);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<Finding?> GetFindingAsync(
        FindingId findingId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                RequirementId,
                Outcome,
                Description
            FROM VeteransClaims_Findings
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            findingId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadFinding(reader);
    }

    public async Task<IReadOnlyList<Finding>> GetFindingsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                RequirementId,
                Outcome,
                Description
            FROM VeteransClaims_Findings
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var findings = new List<Finding>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            findings.Add(ReadFinding(reader));
        }

        return findings;
    }

    private static Finding ReadFinding(
        SqliteDataReader reader)
    {
        return new Finding
        {
            Id = new FindingId(reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(reader.GetString(1)),
            RequirementId =
                reader.IsDBNull(2)
                    ? null
                    : new RequirementId(
                        reader.GetString(2)),
            Outcome = reader.GetString(3),
            Description = reader.GetString(4)
        };
    }
}
