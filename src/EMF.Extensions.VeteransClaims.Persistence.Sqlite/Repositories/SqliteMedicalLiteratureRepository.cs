using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteMedicalLiteratureRepository :
    IMedicalLiteratureRepository
{
    private readonly string _databasePath;

    public SqliteMedicalLiteratureRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection() =>
        VeteransClaimsSqliteConnectionFactory.Create(_databasePath);

    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);

    public async Task AddMedicalLiteratureSourceAsync(
        MedicalLiteratureSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_MedicalLiteratureSources
            (Id, Title, Authors, Publication, PublicationYear,
             VaAffiliated, VaFunded, PeerReviewed,
             FundingSource, ResearchOrganization,
             Doi, Pmid, SourceUri, SourceHash, RetrievedUtc)
            VALUES
            ($id, $title, $authors, $publication, $year,
             $vaAffiliated, $vaFunded, $peerReviewed,
             $fundingSource, $researchOrganization,
             $doi, $pmid, $uri, $hash, $retrieved);
            """;

        command.Parameters.AddWithValue("$id", source.Id.Value);
        command.Parameters.AddWithValue("$title", source.Title);
        command.Parameters.AddWithValue("$authors", source.Authors);
        command.Parameters.AddWithValue(
            "$publication",
            source.Publication);
        command.Parameters.AddWithValue(
            "$year",
            (object?)source.PublicationYear ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$vaAffiliated",
            source.VaAffiliated ? 1 : 0);
        command.Parameters.AddWithValue(
            "$vaFunded",
            source.VaFunded ? 1 : 0);
        command.Parameters.AddWithValue(
            "$peerReviewed",
            source.PeerReviewed ? 1 : 0);
        command.Parameters.AddWithValue(
            "$fundingSource",
            (object?)source.FundingSource ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$researchOrganization",
            (object?)source.ResearchOrganization ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$doi",
            (object?)source.Doi ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$pmid",
            (object?)source.Pmid ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$uri",
            (object?)source.SourceUri ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$hash",
            (object?)source.SourceHash ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$retrieved",
            source.RetrievedUtc?.ToString("O")
                ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MedicalLiteratureSource?>
        GetMedicalLiteratureSourceAsync(
            MedicalLiteratureSourceId sourceId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, Title, Authors, Publication, PublicationYear,
                   VaAffiliated, VaFunded, PeerReviewed,
                   FundingSource, ResearchOrganization,
                   Doi, Pmid, SourceUri, SourceHash, RetrievedUtc
            FROM VeteransClaims_MedicalLiteratureSources
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", sourceId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadSource(reader)
            : null;
    }

    public async Task AddRequirementMedicalLiteratureAsync(
        RequirementMedicalLiterature literature,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(literature);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_RequirementMedicalLiterature
            (RequirementId, MedicalLiteratureSourceId,
             GuidanceRole, Description)
            VALUES ($requirement, $source, $role, $description);
            """;

        command.Parameters.AddWithValue(
            "$requirement",
            literature.RequirementId.Value);
        command.Parameters.AddWithValue(
            "$source",
            literature.MedicalLiteratureSourceId.Value);
        command.Parameters.AddWithValue(
            "$role",
            literature.GuidanceRole);
        command.Parameters.AddWithValue(
            "$description",
            literature.Description);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetRequirementMedicalLiteratureAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT RequirementId, MedicalLiteratureSourceId,
                   GuidanceRole, Description
            FROM VeteransClaims_RequirementMedicalLiterature
            WHERE RequirementId = $requirement
            ORDER BY MedicalLiteratureSourceId, GuidanceRole;
            """;

        command.Parameters.AddWithValue(
            "$requirement",
            requirementId.Value);

        var results =
            new List<RequirementMedicalLiterature>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new RequirementMedicalLiterature
                {
                    RequirementId =
                        new RequirementId(reader.GetString(0)),
                    MedicalLiteratureSourceId =
                        new MedicalLiteratureSourceId(
                            reader.GetString(1)),
                    GuidanceRole = reader.GetString(2),
                    Description = reader.GetString(3)
                });
        }

        return results;
    }

    private static MedicalLiteratureSource ReadSource(
        SqliteDataReader reader) =>
        new()
        {
            Id = new MedicalLiteratureSourceId(reader.GetString(0)),
            Title = reader.GetString(1),
            Authors = reader.GetString(2),
            Publication = reader.GetString(3),
            PublicationYear =
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
            VaAffiliated = reader.GetInt32(5) != 0,
            VaFunded = reader.GetInt32(6) != 0,
            PeerReviewed = reader.GetInt32(7) != 0,
            FundingSource =
                reader.IsDBNull(8) ? null : reader.GetString(8),
            ResearchOrganization =
                reader.IsDBNull(9) ? null : reader.GetString(9),
            Doi = reader.IsDBNull(10) ? null : reader.GetString(10),
            Pmid = reader.IsDBNull(11) ? null : reader.GetString(11),
            SourceUri =
                reader.IsDBNull(12) ? null : reader.GetString(12),
            SourceHash =
                reader.IsDBNull(13) ? null : reader.GetString(13),
            RetrievedUtc =
                reader.IsDBNull(14)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(14))
        };
}
