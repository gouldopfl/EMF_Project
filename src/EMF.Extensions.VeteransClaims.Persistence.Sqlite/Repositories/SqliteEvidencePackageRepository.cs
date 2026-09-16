using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidencePackageRepository :
    IEvidencePackageRepository
{
    private readonly string _databasePath;

    public SqliteEvidencePackageRepository(
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

    public async Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidencePackage);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await InsertEvidencePackageAsync(
            connection,
            null,
            evidencePackage,
            cancellationToken);
    }

    public async Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        IReadOnlyCollection<EvidencePackageArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidencePackage);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (artifacts.Any(
            artifact =>
                artifact.EvidencePackageId !=
                    evidencePackage.Id))
        {
            throw new InvalidOperationException(
                "Every artifact must reference " +
                "the evidence package being persisted.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await InsertEvidencePackageAsync(
            connection,
            transaction,
            evidencePackage,
            cancellationToken);

        foreach (var artifact in artifacts)
        {
            await InsertEvidencePackageArtifactAsync(
                connection,
                transaction,
                artifact,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertEvidencePackageAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        EvidencePackage evidencePackage,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidencePackages (
                Id,
                ClaimIssueId,
                Purpose,
                ReviewerRole,
                ServiceConnectionBasisId,
                CreationOrdinal
            )
            VALUES (
                $id,
                $claimIssueId,
                $purpose,
                $reviewerRole,
                $serviceConnectionBasisId,
                (
                    SELECT
                        COALESCE(MAX(CreationOrdinal), 0) + 1
                    FROM VeteransClaims_EvidencePackages
                )
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidencePackage.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            evidencePackage.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$purpose",
            evidencePackage.Purpose);

        command.Parameters.AddWithValue(
            "$reviewerRole",
            evidencePackage.ReviewerRole);

        command.Parameters.AddWithValue(
            "$serviceConnectionBasisId",
            evidencePackage.ServiceConnectionBasisId.HasValue
                ? evidencePackage.ServiceConnectionBasisId.Value.Value
                : DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<EvidencePackage?> GetEvidencePackageAsync(
        EvidencePackageId evidencePackageId,
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
                Purpose,
                ReviewerRole,
                ServiceConnectionBasisId
            FROM VeteransClaims_EvidencePackages
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidencePackageId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EvidencePackage
        {
            Id =
                new EvidencePackageId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            Purpose =
                reader.GetString(2),
            ReviewerRole =
                reader.GetString(3),
            ServiceConnectionBasisId =
                reader.IsDBNull(4)
                    ? null
                    : new ServiceConnectionBasisId(
                        reader.GetString(4))
        };
    }

    public async Task AddEvidencePackageArtifactAsync(
        EvidencePackageArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await InsertEvidencePackageArtifactAsync(
            connection,
            null,
            artifact,
            cancellationToken);
    }

    private static async Task InsertEvidencePackageArtifactAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        EvidencePackageArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidencePackageArtifacts (
                EvidencePackageId,
                ArtifactId,
                ContentRole,
                ReviewerPageSelection
            )
            VALUES (
                $evidencePackageId,
                $artifactId,
                $contentRole,
                $reviewerPageSelection
            );
            """;

        command.Parameters.AddWithValue(
            "$evidencePackageId",
            artifact.EvidencePackageId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            artifact.ArtifactId.Value);

        command.Parameters.AddWithValue(
            "$contentRole",
            artifact.ContentRole);

        command.Parameters.AddWithValue(
            "$reviewerPageSelection",
            (object?)artifact.ReviewerPageSelection ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task SetReviewerPageSelectionAsync(
        EvidencePackageId evidencePackageId,
        EMF.Core.Models.Identities.ArtifactId artifactId,
        string? reviewerPageSelection,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE VeteransClaims_EvidencePackageArtifacts
            SET ReviewerPageSelection = $reviewerPageSelection
            WHERE EvidencePackageId = $evidencePackageId
              AND ArtifactId = $artifactId;
            """;

        command.Parameters.AddWithValue(
            "$reviewerPageSelection",
            (object?)reviewerPageSelection ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$evidencePackageId",
            evidencePackageId.Value);
        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        var affected =
            await command.ExecuteNonQueryAsync(cancellationToken);

        if (affected != 1)
            throw new InvalidOperationException(
                "Evidence package artifact association was not found.");
    }

    public async Task<IReadOnlyList<EvidencePackageArtifact>>
        GetEvidencePackageArtifactsAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                EvidencePackageId,
                ArtifactId,
                ContentRole,
                ReviewerPageSelection
            FROM VeteransClaims_EvidencePackageArtifacts
            WHERE EvidencePackageId = $evidencePackageId
            ORDER BY ArtifactId, ContentRole;
            """;

        command.Parameters.AddWithValue(
            "$evidencePackageId",
            evidencePackageId.Value);

        var results =
            new List<EvidencePackageArtifact>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidencePackageArtifact
                {
                    EvidencePackageId =
                        new EvidencePackageId(
                            reader.GetString(0)),
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            reader.GetString(1)),
                    ContentRole =
                        reader.GetString(2),
                    ReviewerPageSelection =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetString(3)
                });
        }

        return results;
    }

    public async Task<IReadOnlyList<EvidencePackage>>
        GetEvidencePackagesAsync(
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
                Purpose,
                ReviewerRole,
                ServiceConnectionBasisId
            FROM VeteransClaims_EvidencePackages
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY CreationOrdinal, Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var results =
            new List<EvidencePackage>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidencePackage
                {
                    Id =
                        new EvidencePackageId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    Purpose =
                        reader.GetString(2),
                    ReviewerRole =
                        reader.GetString(3),
                    ServiceConnectionBasisId =
                        reader.IsDBNull(4)
                            ? null
                            : new ServiceConnectionBasisId(
                                reader.GetString(4))
                });
        }

        return results;
    }
}
