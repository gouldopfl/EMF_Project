using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidenceClassificationRepository :
    IEvidenceClassificationRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceClassificationRepository(
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

    public async Task AddEvidenceClassificationAsync(
        EvidenceClassification classification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(classification);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceClassifications (
                Id,
                ArtifactId,
                ClaimIssueId,
                Classification
            )
            VALUES (
                $id,
                $artifactId,
                $claimIssueId,
                $classification
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            classification.Id.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            classification.ArtifactId.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            classification.ClaimIssueId.HasValue
                ? classification.ClaimIssueId.Value.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$classification",
            classification.Classification);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<EvidenceClassification?>
        GetEvidenceClassificationAsync(
            EvidenceClassificationId classificationId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ArtifactId, ClaimIssueId, Classification
            FROM VeteransClaims_EvidenceClassifications
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            classificationId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadEvidenceClassification(reader);
    }

    public async Task<EvidenceClassification?>
        FindEvidenceClassificationAsync(
            ArtifactId artifactId,
            ClaimIssueId? claimIssueId,
            string classification,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classification);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ArtifactId, ClaimIssueId, Classification
            FROM VeteransClaims_EvidenceClassifications
            WHERE ArtifactId = $artifactId
              AND Classification = $classification
              AND (
                    ClaimIssueId = $claimIssueId
                    OR (
                        ClaimIssueId IS NULL
                        AND $claimIssueId IS NULL
                    )
                  )
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        command.Parameters.AddWithValue(
            "$classification",
            classification);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.HasValue
                ? claimIssueId.Value.Value
                : DBNull.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadEvidenceClassification(reader);
    }

    public async Task AddEvidenceClassificationRequirementAsync(
        EvidenceClassificationRequirement association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO
                VeteransClaims_EvidenceClassificationRequirements (
                    EvidenceClassificationId,
                    RequirementId
                )
            VALUES (
                $classificationId,
                $requirementId
            );
            """;

        command.Parameters.AddWithValue(
            "$classificationId",
            association.EvidenceClassificationId.Value);

        command.Parameters.AddWithValue(
            "$requirementId",
            association.RequirementId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceClassificationRequirement>>
        GetEvidenceClassificationRequirementsAsync(
            EvidenceClassificationId classificationId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EvidenceClassificationId, RequirementId
            FROM VeteransClaims_EvidenceClassificationRequirements
            WHERE EvidenceClassificationId = $classificationId
            ORDER BY RequirementId;
            """;

        command.Parameters.AddWithValue(
            "$classificationId",
            classificationId.Value);

        var results =
            new List<EvidenceClassificationRequirement>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidenceClassificationRequirement
                {
                    EvidenceClassificationId =
                        new EvidenceClassificationId(
                            reader.GetString(0)),
                    RequirementId =
                        new RequirementId(
                            reader.GetString(1))
                });
        }

        return results;
    }

    public async Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.Id, c.ArtifactId, c.ClaimIssueId, c.Classification
            FROM VeteransClaims_EvidenceClassifications c
            INNER JOIN VeteransClaims_EvidenceClassificationRequirements r
                ON r.EvidenceClassificationId = c.Id
            WHERE r.RequirementId = $requirementId
            ORDER BY c.Id;
            """;

        command.Parameters.AddWithValue(
            "$requirementId",
            requirementId.Value);

        return await ReadEvidenceClassificationsAsync(
            command,
            cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ArtifactId, ClaimIssueId, Classification
            FROM VeteransClaims_EvidenceClassifications
            WHERE ArtifactId = $artifactId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        return await ReadEvidenceClassificationsAsync(
            command,
            cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ArtifactId, ClaimIssueId, Classification
            FROM VeteransClaims_EvidenceClassifications
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        return await ReadEvidenceClassificationsAsync(
            command,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<EvidenceClassification>>
        ReadEvidenceClassificationsAsync(
            SqliteCommand command,
            CancellationToken cancellationToken)
    {
        var classifications =
            new List<EvidenceClassification>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            classifications.Add(
                ReadEvidenceClassification(reader));
        }

        return classifications;
    }

    private static EvidenceClassification
        ReadEvidenceClassification(
            SqliteDataReader reader)
    {
        return new EvidenceClassification
        {
            Id =
                new EvidenceClassificationId(
                    reader.GetString(0)),
            ArtifactId =
                new ArtifactId(
                    reader.GetString(1)),
            ClaimIssueId =
                reader.IsDBNull(2)
                    ? null
                    : new ClaimIssueId(
                        reader.GetString(2)),
            Classification = reader.GetString(3)
        };
    }
}
