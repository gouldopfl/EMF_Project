using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidenceRequirementGuidanceRepository :
    IEvidenceRequirementGuidanceRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceRequirementGuidanceRepository(string databasePath)
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

    public async Task AddEvidenceRequirementGuidanceAsync(
        EvidenceRequirementGuidance guidance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guidance);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_EvidenceRequirementGuidance
            (Id, RequirementId, EvidenceClassification, GuidanceRole, Description)
            VALUES ($id, $requirementId, $classification, $role, $description);
            """;

        command.Parameters.AddWithValue("$id", guidance.Id.Value);
        command.Parameters.AddWithValue("$requirementId", guidance.RequirementId.Value);
        command.Parameters.AddWithValue("$classification", guidance.EvidenceClassification);
        command.Parameters.AddWithValue("$role", guidance.GuidanceRole);
        command.Parameters.AddWithValue("$description", guidance.Description);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EvidenceRequirementGuidance?>
        GetEvidenceRequirementGuidanceAsync(
            EvidenceRequirementGuidanceId guidanceId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, RequirementId, EvidenceClassification,
                   GuidanceRole, Description
            FROM VeteransClaims_EvidenceRequirementGuidance
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", guidanceId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadGuidance(reader)
            : null;
    }

    public async Task<IReadOnlyList<EvidenceRequirementGuidance>>
        GetEvidenceRequirementGuidanceAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, RequirementId, EvidenceClassification,
                   GuidanceRole, Description
            FROM VeteransClaims_EvidenceRequirementGuidance
            WHERE RequirementId = $requirementId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$requirementId",
            requirementId.Value);

        var results = new List<EvidenceRequirementGuidance>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadGuidance(reader));

        return results;
    }

    private static EvidenceRequirementGuidance ReadGuidance(
        SqliteDataReader reader) =>
        new()
        {
            Id = new EvidenceRequirementGuidanceId(reader.GetString(0)),
            RequirementId = new RequirementId(reader.GetString(1)),
            EvidenceClassification = reader.GetString(2),
            GuidanceRole = reader.GetString(3),
            Description = reader.GetString(4)
        };
}
