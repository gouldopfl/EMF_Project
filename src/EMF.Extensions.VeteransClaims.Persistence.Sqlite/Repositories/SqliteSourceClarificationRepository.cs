using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteSourceClarificationRepository :
    ISourceClarificationRepository
{
    private readonly string _databasePath;

    public SqliteSourceClarificationRepository(string databasePath)
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

    public async Task AddAsync(
        SourceClarification clarification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        Validate(clarification);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO VeteransClaims_SourceClarifications (
                Id, ClaimIssueId, SourceArtifactId, EvidenceDate,
                SourceStartPage, SourceEndPage, RecordTitle, Category,
                OriginalText, Clarification, ReviewerMatchText,
                ReviewerReplacementText
            )
            VALUES (
                $id, $claimIssueId, $sourceArtifactId, $evidenceDate,
                $sourceStartPage, $sourceEndPage, $recordTitle, $category,
                $originalText, $clarification, $reviewerMatchText,
                $reviewerReplacementText
            );
            """;

        command.Parameters.AddWithValue("$id", clarification.Id.Value);
        command.Parameters.AddWithValue(
            "$claimIssueId", clarification.ClaimIssueId.Value);
        command.Parameters.AddWithValue(
            "$sourceArtifactId", clarification.SourceArtifactId.Value);
        command.Parameters.AddWithValue(
            "$evidenceDate", clarification.EvidenceDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$sourceStartPage", clarification.SourceStartPage);
        command.Parameters.AddWithValue(
            "$sourceEndPage", clarification.SourceEndPage);
        command.Parameters.AddWithValue(
            "$recordTitle", clarification.RecordTitle.Trim());
        command.Parameters.AddWithValue(
            "$category", clarification.Category.Trim());
        command.Parameters.AddWithValue(
            "$originalText", clarification.OriginalText.Trim());
        command.Parameters.AddWithValue(
            "$clarification", clarification.Clarification.Trim());
        command.Parameters.AddWithValue(
            "$reviewerMatchText",
            (object?)clarification.ReviewerMatchText?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$reviewerReplacementText",
            (object?)clarification.ReviewerReplacementText?.Trim() ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceClarification>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, ClaimIssueId, SourceArtifactId, EvidenceDate,
                   SourceStartPage, SourceEndPage, RecordTitle, Category,
                   OriginalText, Clarification, ReviewerMatchText,
                   ReviewerReplacementText
            FROM VeteransClaims_SourceClarifications
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY EvidenceDate, SourceStartPage, Id;
            """;

        command.Parameters.AddWithValue("$claimIssueId", claimIssueId.Value);

        var result = new List<SourceClarification>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new SourceClarification
                {
                    Id = new SourceClarificationId(reader.GetString(0)),
                    ClaimIssueId = new ClaimIssueId(reader.GetString(1)),
                    SourceArtifactId = new ArtifactId(reader.GetString(2)),
                    EvidenceDate = DateOnly.Parse(reader.GetString(3)),
                    SourceStartPage = reader.GetInt32(4),
                    SourceEndPage = reader.GetInt32(5),
                    RecordTitle = reader.GetString(6),
                    Category = reader.GetString(7),
                    OriginalText = reader.GetString(8),
                    Clarification = reader.GetString(9),
                    ReviewerMatchText =
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                    ReviewerReplacementText =
                        reader.IsDBNull(11) ? null : reader.GetString(11)
                });
        }

        return result;
    }

    public async Task SetReviewerCorrectionAsync(
        SourceClarificationId sourceClarificationId,
        string reviewerMatchText,
        string reviewerReplacementText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerMatchText);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerReplacementText);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            UPDATE VeteransClaims_SourceClarifications
            SET ReviewerMatchText = $reviewerMatchText,
                ReviewerReplacementText = $reviewerReplacementText
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id", sourceClarificationId.Value);
        command.Parameters.AddWithValue(
            "$reviewerMatchText", reviewerMatchText.Trim());
        command.Parameters.AddWithValue(
            "$reviewerReplacementText", reviewerReplacementText.Trim());

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);

        if (affected != 1)
            throw new InvalidOperationException(
                "Source clarification reviewer correction target was not found.");
    }

    private static void Validate(SourceClarification clarification)
    {
        if (clarification.SourceStartPage <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clarification.SourceStartPage));
        }

        if (clarification.SourceEndPage < clarification.SourceStartPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clarification.SourceEndPage));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.RecordTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.OriginalText);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.Clarification);

        var hasReviewerMatch =
            !string.IsNullOrWhiteSpace(clarification.ReviewerMatchText);
        var hasReviewerReplacement =
            !string.IsNullOrWhiteSpace(clarification.ReviewerReplacementText);

        if (hasReviewerMatch != hasReviewerReplacement)
            throw new ArgumentException(
                "Reviewer source correction requires both match and replacement text.");

        if (!SourceClarificationCategories.IsSupported(
                clarification.Category.Trim()))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clarification.Category),
                clarification.Category,
                "Unsupported source clarification category.");
        }
    }
}
