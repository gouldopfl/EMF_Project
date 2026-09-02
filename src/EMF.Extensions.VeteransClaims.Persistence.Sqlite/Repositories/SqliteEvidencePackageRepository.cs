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
                ReviewerRole
            )
            VALUES (
                $id,
                $claimIssueId,
                $purpose,
                $reviewerRole
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
            SELECT Id, ClaimIssueId, Purpose, ReviewerRole
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
                reader.GetString(3)
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
                ContentRole
            )
            VALUES (
                $evidencePackageId,
                $artifactId,
                $contentRole
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

        await command.ExecuteNonQueryAsync(
            cancellationToken);
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
            SELECT EvidencePackageId, ArtifactId, ContentRole
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
                        reader.GetString(2)
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
            SELECT Id, ClaimIssueId, Purpose, ReviewerRole
            FROM VeteransClaims_EvidencePackages
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
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
                        reader.GetString(3)
                });
        }

        return results;
    }
}
