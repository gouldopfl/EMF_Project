using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidenceGapRepository :
    IEvidenceGapRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceGapRepository(string databasePath)
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

    public async Task AddEvidenceGapAsync(
        EvidenceGap evidenceGap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidenceGap);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceGaps (
                Id,
                ClaimIssueId,
                RequirementId,
                Description,
                Status
            )
            VALUES (
                $id,
                $claimIssueId,
                $requirementId,
                $description,
                $status
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidenceGap.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            evidenceGap.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$requirementId",
            evidenceGap.RequirementId.Value);

        command.Parameters.AddWithValue(
            "$description",
            evidenceGap.Description);

        command.Parameters.AddWithValue(
            "$status",
            evidenceGap.Status);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<EvidenceGap?> GetEvidenceGapAsync(
        EvidenceGapId evidenceGapId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, RequirementId, Description, Status
            FROM VeteransClaims_EvidenceGaps
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidenceGapId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EvidenceGap
        {
            Id = new EvidenceGapId(reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(reader.GetString(1)),
            RequirementId =
                new RequirementId(reader.GetString(2)),
            Description = reader.GetString(3),
            Status = reader.GetString(4)
        };
    }

    public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        return GetEvidenceGapsAsync(
            "ClaimIssueId",
            claimIssueId.Value,
            cancellationToken);
    }

    public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
        RequirementId requirementId,
        CancellationToken cancellationToken = default)
    {
        return GetEvidenceGapsAsync(
            "RequirementId",
            requirementId.Value,
            cancellationToken);
    }

    public async Task AddEvidenceGapArtifactAsync(
        EvidenceGapArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceGapArtifacts (
                EvidenceGapId,
                ArtifactId,
                Role
            )
            VALUES (
                $evidenceGapId,
                $artifactId,
                $role
            );
            """;

        command.Parameters.AddWithValue(
            "$evidenceGapId",
            artifact.EvidenceGapId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            artifact.ArtifactId.Value);

        command.Parameters.AddWithValue(
            "$role",
            artifact.Role);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceGapArtifact>>
        GetEvidenceGapArtifactsAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EvidenceGapId, ArtifactId, Role
            FROM VeteransClaims_EvidenceGapArtifacts
            WHERE EvidenceGapId = $evidenceGapId
            ORDER BY ArtifactId, Role;
            """;

        command.Parameters.AddWithValue(
            "$evidenceGapId",
            evidenceGapId.Value);

        var results =
            new List<EvidenceGapArtifact>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidenceGapArtifact
                {
                    EvidenceGapId =
                        new EvidenceGapId(
                            reader.GetString(0)),
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            reader.GetString(1)),
                    Role =
                        reader.GetString(2)
                });
        }

        return results;
    }

    private async Task<IReadOnlyList<EvidenceGap>>
        GetEvidenceGapsAsync(
            string columnName,
            string value,
            CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT Id, ClaimIssueId, RequirementId, Description, Status
            FROM VeteransClaims_EvidenceGaps
            WHERE {columnName} = $value
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$value",
            value);

        var gaps = new List<EvidenceGap>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            gaps.Add(
                new EvidenceGap
                {
                    Id = new EvidenceGapId(
                        reader.GetString(0)),
                    ClaimIssueId = new ClaimIssueId(
                        reader.GetString(1)),
                    RequirementId = new RequirementId(
                        reader.GetString(2)),
                    Description = reader.GetString(3),
                    Status = reader.GetString(4)
                });
        }

        return gaps;
    }


    public async Task UpdateEvidenceGapStatusAsync(
        EvidenceGapId evidenceGapId,
        string status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE VeteransClaims_EvidenceGaps
            SET Status = $status
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidenceGapId.Value);

        command.Parameters.AddWithValue(
            "$status",
            status);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

}
