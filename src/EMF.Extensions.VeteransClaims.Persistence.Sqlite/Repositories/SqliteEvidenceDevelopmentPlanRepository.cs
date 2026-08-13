using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidenceDevelopmentPlanRepository :
    IEvidenceDevelopmentPlanRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceDevelopmentPlanRepository(
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
        return new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);
    }

    public async Task AddEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlans (
                Id,
                ClaimIssueId,
                Description
            )
            VALUES (
                $id,
                $claimIssueId,
                $description
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            plan.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            plan.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$description",
            plan.Description);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<EvidenceDevelopmentPlan?>
        GetEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Description
            FROM VeteransClaims_EvidenceDevelopmentPlans
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            planId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EvidenceDevelopmentPlan
        {
            Id = new EvidenceDevelopmentPlanId(
                reader.GetString(0)),
            ClaimIssueId = new ClaimIssueId(
                reader.GetString(1)),
            Description = reader.GetString(2)
        };
    }

    public async Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        GetEvidenceDevelopmentPlansAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Description
            FROM VeteransClaims_EvidenceDevelopmentPlans
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var plans = new List<EvidenceDevelopmentPlan>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            plans.Add(
                new EvidenceDevelopmentPlan
                {
                    Id = new EvidenceDevelopmentPlanId(
                        reader.GetString(0)),
                    ClaimIssueId = new ClaimIssueId(
                        reader.GetString(1)),
                    Description = reader.GetString(2)
                });
        }

        return plans;
    }
}
