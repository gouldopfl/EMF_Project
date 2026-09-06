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


    public async Task AddFindingRegulatoryProvisionAsync(
        FindingRegulatoryProvision association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        ValidateTraceabilityRole(association.Role);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_FindingRegulatoryProvisions (
                FindingId, RegulatoryProvisionId, Role
            )
            SELECT $findingId, $regulatoryProvisionId, $role
            FROM VeteransClaims_Findings AS finding
            INNER JOIN VeteransClaims_RegulatoryProvisions AS provision
                ON provision.Id = $regulatoryProvisionId
            WHERE finding.Id = $findingId;
            """;

        command.Parameters.AddWithValue(
            "$findingId", association.FindingId.Value);
        command.Parameters.AddWithValue(
            "$regulatoryProvisionId",
            association.RegulatoryProvisionId.Value);
        command.Parameters.AddWithValue(
            "$role", association.Role);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The finding and regulatory provision must exist.");
        }
    }

    public Task<IReadOnlyList<FindingRegulatoryProvision>>
        GetFindingRegulatoryProvisionsAsync(
            FindingId findingId,
            CancellationToken cancellationToken = default)
    {
        return GetFindingRegulatoryProvisionsAsync(
            "FindingId",
            findingId.Value,
            cancellationToken);
    }

    public Task<IReadOnlyList<FindingRegulatoryProvision>>
        GetFindingRegulatoryProvisionsAsync(
            RegulatoryProvisionId regulatoryProvisionId,
            CancellationToken cancellationToken = default)
    {
        return GetFindingRegulatoryProvisionsAsync(
            "RegulatoryProvisionId",
            regulatoryProvisionId.Value,
            cancellationToken);
    }

    private async Task<IReadOnlyList<FindingRegulatoryProvision>>
        GetFindingRegulatoryProvisionsAsync(
            string columnName,
            string value,
            CancellationToken cancellationToken)
    {
        if (columnName != "FindingId" &&
            columnName != "RegulatoryProvisionId")
        {
            throw new ArgumentOutOfRangeException(nameof(columnName));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT FindingId, RegulatoryProvisionId, Role
            FROM VeteransClaims_FindingRegulatoryProvisions
            WHERE {columnName} = $value
            ORDER BY FindingId, RegulatoryProvisionId, Role;
            """;
        command.Parameters.AddWithValue("$value", value);

        var results = new List<FindingRegulatoryProvision>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(2);
            ValidateStoredTraceabilityRole(role);

            results.Add(
                new FindingRegulatoryProvision
                {
                    FindingId = new FindingId(reader.GetString(0)),
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId(reader.GetString(1)),
                    Role = role
                });
        }

        return results;
    }

    private static void ValidateTraceabilityRole(string role)
    {
        if (role != FindingTraceabilityRoles.Supporting &&
            role != FindingTraceabilityRoles.Contradicting &&
            role != FindingTraceabilityRoles.Qualifying)
        {
            throw new ArgumentException(
                "Finding regulatory provision role is invalid.",
                nameof(role));
        }
    }

    private static void ValidateStoredTraceabilityRole(string role)
    {
        if (role != FindingTraceabilityRoles.Supporting &&
            role != FindingTraceabilityRoles.Contradicting &&
            role != FindingTraceabilityRoles.Qualifying)
        {
            throw new InvalidOperationException(
                "Stored finding regulatory provision role is invalid.");
        }
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
