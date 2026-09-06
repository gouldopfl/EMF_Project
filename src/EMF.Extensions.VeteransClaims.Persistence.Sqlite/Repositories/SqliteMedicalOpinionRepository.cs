using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteMedicalOpinionRepository :
    IMedicalOpinionRepository
{
    private readonly string _databasePath;

    public SqliteMedicalOpinionRepository(string databasePath)
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

    public async Task AddMedicalOpinionAsync(
        MedicalOpinion medicalOpinion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(medicalOpinion);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicalOpinions (
                Id,
                ClaimIssueId,
                Question,
                Opinion
            )
            VALUES (
                $id,
                $claimIssueId,
                $question,
                $opinion
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            medicalOpinion.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            medicalOpinion.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$question",
            medicalOpinion.Question);

        command.Parameters.AddWithValue(
            "$opinion",
            medicalOpinion.Opinion);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<MedicalOpinion?> GetMedicalOpinionAsync(
        MedicalOpinionId medicalOpinionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Question, Opinion
            FROM VeteransClaims_MedicalOpinions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            medicalOpinionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadMedicalOpinion(reader);
    }

    public async Task<IReadOnlyList<MedicalOpinion>>
        GetMedicalOpinionsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Question, Opinion
            FROM VeteransClaims_MedicalOpinions
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var opinions = new List<MedicalOpinion>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            opinions.Add(ReadMedicalOpinion(reader));
        }

        return opinions;
    }

    public async Task AddMedicalOpinionArtifactAsync(
        MedicalOpinionArtifact association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicalOpinionArtifacts (
                MedicalOpinionId,
                ArtifactId
            )
            SELECT opinion.Id, artifact.Id
            FROM VeteransClaims_MedicalOpinions AS opinion
            INNER JOIN Artifacts AS artifact
                ON artifact.Id = $artifactId
            WHERE opinion.Id = $medicalOpinionId;
            """;

        command.Parameters.AddWithValue(
            "$medicalOpinionId",
            association.MedicalOpinionId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            association.ArtifactId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The medical opinion and artifact must exist.");
        }
    }

    public async Task<IReadOnlyList<ArtifactId>> GetArtifactIdsAsync(
        MedicalOpinionId medicalOpinionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ArtifactId
            FROM VeteransClaims_MedicalOpinionArtifacts
            WHERE MedicalOpinionId = $medicalOpinionId
            ORDER BY ArtifactId;
            """;

        command.Parameters.AddWithValue(
            "$medicalOpinionId",
            medicalOpinionId.Value);

        var artifactIds = new List<ArtifactId>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            artifactIds.Add(new ArtifactId(reader.GetString(0)));
        }

        return artifactIds;
    }

    public async Task<IReadOnlyList<MedicalOpinionId>>
        GetMedicalOpinionIdsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MedicalOpinionId
            FROM VeteransClaims_MedicalOpinionArtifacts
            WHERE ArtifactId = $artifactId
            ORDER BY MedicalOpinionId;
            """;

        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        var medicalOpinionIds = new List<MedicalOpinionId>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            medicalOpinionIds.Add(
                new MedicalOpinionId(reader.GetString(0)));
        }

        return medicalOpinionIds;
    }

    private static MedicalOpinion ReadMedicalOpinion(
        SqliteDataReader reader)
    {
        return new MedicalOpinion
        {
            Id = new MedicalOpinionId(reader.GetString(0)),
            ClaimIssueId = new ClaimIssueId(reader.GetString(1)),
            Question = reader.GetString(2),
            Opinion = reader.GetString(3)
        };
    }
}
