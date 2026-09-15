using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteClinicalProgressionRepository :
    IClinicalProgressionRepository
{
    private readonly string _databasePath;

    public SqliteClinicalProgressionRepository(string databasePath)
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
        ClinicalProgressionEvent progressionEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progressionEvent);
        Validate(progressionEvent);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO VeteransClaims_ClinicalProgressionEvents (
                Id, ClaimIssueId, SourceArtifactId, EventDate,
                SourceStartPage, SourceEndPage, RecordTitle,
                EventType, Summary
            )
            VALUES (
                $id, $claimIssueId, $sourceArtifactId, $eventDate,
                $sourceStartPage, $sourceEndPage, $recordTitle,
                $eventType, $summary
            );
            """;

        command.Parameters.AddWithValue("$id", progressionEvent.Id.Value);
        command.Parameters.AddWithValue(
            "$claimIssueId", progressionEvent.ClaimIssueId.Value);
        command.Parameters.AddWithValue(
            "$sourceArtifactId", progressionEvent.SourceArtifactId.Value);
        command.Parameters.AddWithValue(
            "$eventDate", progressionEvent.EventDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$sourceStartPage",
            (object?)progressionEvent.SourceStartPage ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$sourceEndPage",
            (object?)progressionEvent.SourceEndPage ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$recordTitle", progressionEvent.RecordTitle.Trim());
        command.Parameters.AddWithValue(
            "$eventType", progressionEvent.EventType.Trim());
        command.Parameters.AddWithValue(
            "$summary", progressionEvent.Summary.Trim());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicalProgressionEvent>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, ClaimIssueId, SourceArtifactId, EventDate,
                   SourceStartPage, SourceEndPage, RecordTitle,
                   EventType, Summary
            FROM VeteransClaims_ClinicalProgressionEvents
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY EventDate, COALESCE(SourceStartPage, 0), Id;
            """;

        command.Parameters.AddWithValue("$claimIssueId", claimIssueId.Value);

        var result = new List<ClinicalProgressionEvent>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new ClinicalProgressionEvent
                {
                    Id = new ClinicalProgressionEventId(reader.GetString(0)),
                    ClaimIssueId = new ClaimIssueId(reader.GetString(1)),
                    SourceArtifactId = new ArtifactId(reader.GetString(2)),
                    EventDate = DateOnly.Parse(reader.GetString(3)),
                    SourceStartPage =
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    SourceEndPage =
                        reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    RecordTitle = reader.GetString(6),
                    EventType = reader.GetString(7),
                    Summary = reader.GetString(8)
                });
        }

        return result;
    }

    private static void Validate(ClinicalProgressionEvent progressionEvent)
    {
        var hasStart = progressionEvent.SourceStartPage.HasValue;
        var hasEnd = progressionEvent.SourceEndPage.HasValue;

        if (hasStart != hasEnd)
        {
            throw new ArgumentException(
                "Clinical progression source pages must both be supplied or both omitted.");
        }

        if (progressionEvent.SourceStartPage is int startPage && startPage <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressionEvent.SourceStartPage));
        }

        if (progressionEvent.SourceEndPage is int endPage &&
            progressionEvent.SourceStartPage is int validStartPage &&
            endPage < validStartPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressionEvent.SourceEndPage));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(progressionEvent.RecordTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(progressionEvent.EventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(progressionEvent.Summary);

        if (!ClinicalProgressionEventTypes.IsSupported(
                progressionEvent.EventType.Trim()))
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressionEvent.EventType),
                progressionEvent.EventType,
                "Unsupported clinical progression event type.");
        }
    }
}
