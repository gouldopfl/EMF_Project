using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidenceRecognitionTermRepository :
    IEvidenceRecognitionTermRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceRecognitionTermRepository(string databasePath)
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

    public async Task AddEvidenceRecognitionTermAsync(
        EvidenceRecognitionTerm term,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(term);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_EvidenceRecognitionTerms
            (Id, RequirementId, Term, TermType, RecognitionRole, AuthoritySource)
            VALUES ($id, $requirementId, $term, $type, $role, $authority);
            """;

        command.Parameters.AddWithValue("$id", term.Id.Value);
        command.Parameters.AddWithValue("$requirementId", term.RequirementId.Value);
        command.Parameters.AddWithValue("$term", term.Term);
        command.Parameters.AddWithValue("$type", term.TermType);
        command.Parameters.AddWithValue("$role", term.RecognitionRole);
        command.Parameters.AddWithValue("$authority", term.AuthoritySource);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EvidenceRecognitionTerm?>
        GetEvidenceRecognitionTermAsync(
            EvidenceRecognitionTermId termId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, RequirementId, Term, TermType,
                   RecognitionRole, AuthoritySource
            FROM VeteransClaims_EvidenceRecognitionTerms
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", termId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadTerm(reader)
            : null;
    }

    public async Task<IReadOnlyList<EvidenceRecognitionTerm>>
        GetEvidenceRecognitionTermsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, RequirementId, Term, TermType,
                   RecognitionRole, AuthoritySource
            FROM VeteransClaims_EvidenceRecognitionTerms
            WHERE RequirementId = $requirementId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$requirementId",
            requirementId.Value);

        var terms = new List<EvidenceRecognitionTerm>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            terms.Add(ReadTerm(reader));

        return terms;
    }

    private static EvidenceRecognitionTerm ReadTerm(
        SqliteDataReader reader) =>
        new()
        {
            Id = new EvidenceRecognitionTermId(reader.GetString(0)),
            RequirementId = new RequirementId(reader.GetString(1)),
            Term = reader.GetString(2),
            TermType = reader.GetString(3),
            RecognitionRole = reader.GetString(4),
            AuthoritySource = reader.GetString(5)
        };
}
