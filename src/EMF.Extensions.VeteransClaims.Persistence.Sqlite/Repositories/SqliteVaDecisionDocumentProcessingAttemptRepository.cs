using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteVaDecisionDocumentProcessingAttemptRepository :
    IVaDecisionDocumentProcessingAttemptRepository
{
    private readonly string _databasePath;

    public SqliteVaDecisionDocumentProcessingAttemptRepository(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection() =>
        VeteransClaimsSqliteConnectionFactory.Create(_databasePath);

    public async Task AddAsync(
        VaDecisionDocumentProcessingAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_VaDecisionDocumentProcessingAttempts (
                ClaimId,
                ArtifactId,
                ProcessedAt,
                VaDecisionId
            )
            VALUES (
                $claimId,
                $artifactId,
                $processedAt,
                $vaDecisionId
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue(
            "$claimId",
            attempt.ClaimId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            attempt.ArtifactId.Value);

        command.Parameters.AddWithValue(
            "$processedAt",
            attempt.ProcessedAt.ToString("O"));

        command.Parameters.AddWithValue(
            "$vaDecisionId",
            attempt.VaDecisionId is null
                ? DBNull.Value
                : attempt.VaDecisionId.Value.Value);

        var attemptId =
            Convert.ToInt64(
                await command.ExecuteScalarAsync(
                    cancellationToken));

        for (var i = 0; i < attempt.Matches.Count; i++)
        {
            var match = attempt.Matches[i];

            await using var matchCommand =
                connection.CreateCommand();

            matchCommand.CommandText =
                """
                INSERT INTO VeteransClaims_VaDecisionDocumentIssueMatches (
                    ProcessingAttemptId,
                    MatchOrdinal,
                    Status,
                    ClaimIssueId,
                    IssueDescription,
                    Outcome,
                    Rationale
                )
                VALUES (
                    $attemptId,
                    $ordinal,
                    $status,
                    $claimIssueId,
                    $description,
                    $outcome,
                    $rationale
                );

                SELECT last_insert_rowid();
                """;

            matchCommand.Parameters.AddWithValue(
                "$attemptId", attemptId);
            matchCommand.Parameters.AddWithValue(
                "$ordinal", i);
            matchCommand.Parameters.AddWithValue(
                "$status", match.Status);
            matchCommand.Parameters.AddWithValue(
                "$claimIssueId",
                match.ClaimIssueId is null
                    ? DBNull.Value
                    : match.ClaimIssueId.Value.Value);
            matchCommand.Parameters.AddWithValue(
                "$description",
                match.Interpretation.IssueDescription);
            matchCommand.Parameters.AddWithValue(
                "$outcome",
                match.Interpretation.Outcome);
            matchCommand.Parameters.AddWithValue(
                "$rationale",
                match.Interpretation.Rationale);

            var matchId =
                Convert.ToInt64(
                    await matchCommand.ExecuteScalarAsync(
                        cancellationToken));

            await InsertValuesAsync(
                connection,
                matchId,
                "FavorableFinding",
                match.Interpretation.FavorableFindings,
                cancellationToken);

            await InsertValuesAsync(
                connection,
                matchId,
                "AdverseFinding",
                match.Interpretation.AdverseFindings,
                cancellationToken);

            await InsertValuesAsync(
                connection,
                matchId,
                "CitedRegulation",
                match.Interpretation.CitedRegulations,
                cancellationToken);

            await InsertValuesAsync(
                connection,
                matchId,
                "ReferencedEvidence",
                match.Interpretation.ReferencedEvidence,
                cancellationToken);

            await InsertSourceExcerptsAsync(
                connection,
                matchId,
                match.Interpretation.SourceExcerpts,
                cancellationToken);

            for (var j = 0;
                 j < match.CandidateClaimIssueIds.Count;
                 j++)
            {
                await using var valueCommand =
                    connection.CreateCommand();

                valueCommand.CommandText =
                    """
                    INSERT INTO VeteransClaims_VaDecisionDocumentMatchValues (
                        IssueMatchId,
                        ValueKind,
                        ValueOrdinal,
                        Value
                    )
                    VALUES (
                        $matchId,
                        'CandidateClaimIssueId',
                        $ordinal,
                        $value
                    );
                    """;

                valueCommand.Parameters.AddWithValue(
                    "$matchId", matchId);
                valueCommand.Parameters.AddWithValue(
                    "$ordinal", j);
                valueCommand.Parameters.AddWithValue(
                    "$value",
                    match.CandidateClaimIssueIds[j].Value);

                await valueCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }
        }
    }

    private static async Task<IReadOnlyList<DecisionDocumentSourceExcerpt>>
        GetSourceExcerptsAsync(
            SqliteConnection connection,
            long matchId,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT ArtifactId, Text, StartOffset, Length
            FROM VeteransClaims_VaDecisionDocumentSourceExcerpts
            WHERE IssueMatchId = $matchId
            ORDER BY ExcerptOrdinal;
            """;

        command.Parameters.AddWithValue("$matchId", matchId);

        var excerpts =
            new List<DecisionDocumentSourceExcerpt>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            excerpts.Add(
                new DecisionDocumentSourceExcerpt
                {
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            reader.GetString(0)),
                    Text = reader.GetString(1),
                    StartOffset =
                        reader.IsDBNull(2)
                            ? null
                            : reader.GetInt32(2),
                    Length =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetInt32(3)
                });
        }

        return excerpts;
    }


    private static async Task<IReadOnlyList<string>>
        GetValuesAsync(
            SqliteConnection connection,
            long matchId,
            string kind,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Value
            FROM VeteransClaims_VaDecisionDocumentMatchValues
            WHERE IssueMatchId = $matchId
              AND ValueKind = $kind
            ORDER BY ValueOrdinal;
            """;

        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$kind", kind);

        var values = new List<string>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            values.Add(reader.GetString(0));

        return values;
    }


    private static async Task<IReadOnlyList<VaDecisionDocumentIssueMatch>>
        GetMatchesAsync(
            SqliteConnection connection,
            long attemptId,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                Id,
                Status,
                ClaimIssueId,
                IssueDescription,
                Outcome,
                Rationale
            FROM VeteransClaims_VaDecisionDocumentIssueMatches
            WHERE ProcessingAttemptId = $attemptId
            ORDER BY MatchOrdinal;
            """;

        command.Parameters.AddWithValue("$attemptId", attemptId);

        var matches = new List<VaDecisionDocumentIssueMatch>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(
                new VaDecisionDocumentIssueMatch
                {
                    Status = reader.GetString(1),
                    ClaimIssueId =
                        reader.IsDBNull(2)
                            ? null
                            : new ClaimIssueId(reader.GetString(2)),
                    CandidateClaimIssueIds =
                        (await GetValuesAsync(
                            connection,
                            reader.GetInt64(0),
                            "CandidateClaimIssueId",
                            cancellationToken))
                        .Select(value => new ClaimIssueId(value))
                        .ToArray(),
                    Interpretation =
                        new VaIssueDecisionInterpretation
                        {
                            IssueDescription = reader.GetString(3),
                            Outcome = reader.GetString(4),
                            Rationale = reader.GetString(5),
                            FavorableFindings =
                                await GetValuesAsync(
                                    connection, reader.GetInt64(0),
                                    "FavorableFinding",
                                    cancellationToken),
                            AdverseFindings =
                                await GetValuesAsync(
                                    connection, reader.GetInt64(0),
                                    "AdverseFinding",
                                    cancellationToken),
                            CitedRegulations =
                                await GetValuesAsync(
                                    connection, reader.GetInt64(0),
                                    "CitedRegulation",
                                    cancellationToken),
                            ReferencedEvidence =
                                await GetValuesAsync(
                                    connection, reader.GetInt64(0),
                                    "ReferencedEvidence",
                                    cancellationToken),
                            SourceExcerpts =
                                await GetSourceExcerptsAsync(
                                    connection,
                                    reader.GetInt64(0),
                                    cancellationToken)
                        }
                });
        }

        return matches;
    }


    private static async Task InsertSourceExcerptsAsync(
        SqliteConnection connection,
        long matchId,
        IReadOnlyList<DecisionDocumentSourceExcerpt> excerpts,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < excerpts.Count; i++)
        {
            var excerpt = excerpts[i];

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO VeteransClaims_VaDecisionDocumentSourceExcerpts (
                    IssueMatchId,
                    ExcerptOrdinal,
                    ArtifactId,
                    Text,
                    StartOffset,
                    Length
                )
                VALUES (
                    $matchId,
                    $ordinal,
                    $artifactId,
                    $text,
                    $startOffset,
                    $length
                );
                """;

            command.Parameters.AddWithValue("$matchId", matchId);
            command.Parameters.AddWithValue("$ordinal", i);
            command.Parameters.AddWithValue("$artifactId", excerpt.ArtifactId.Value);
            command.Parameters.AddWithValue("$text", excerpt.Text);
            command.Parameters.AddWithValue(
                "$startOffset",
                excerpt.StartOffset is null ? DBNull.Value : excerpt.StartOffset.Value);
            command.Parameters.AddWithValue(
                "$length",
                excerpt.Length is null ? DBNull.Value : excerpt.Length.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }


    private static async Task InsertValuesAsync(
        SqliteConnection connection,
        long matchId,
        string kind,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < values.Count; i++)
        {
            await using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO VeteransClaims_VaDecisionDocumentMatchValues (
                    IssueMatchId, ValueKind, ValueOrdinal, Value
                )
                VALUES ($matchId, $kind, $ordinal, $value);
                """;

            command.Parameters.AddWithValue("$matchId", matchId);
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$ordinal", i);
            command.Parameters.AddWithValue("$value", values[i]);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }


    public async Task<IReadOnlyList<VaDecisionDocumentProcessingAttempt>>
        GetByClaimAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ArtifactId, ProcessedAt, VaDecisionId
            FROM VeteransClaims_VaDecisionDocumentProcessingAttempts
            WHERE ClaimId = $claimId
            ORDER BY ProcessedAt, Id;
            """;

        command.Parameters.AddWithValue(
            "$claimId",
            claimId.Value);

        var attempts =
            new List<VaDecisionDocumentProcessingAttempt>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = claimId,
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            reader.GetString(1)),
                    ProcessedAt =
                        DateTimeOffset.Parse(reader.GetString(2)),
                    VaDecisionId =
                        reader.IsDBNull(3)
                            ? null
                            : new VaDecisionId(
                                reader.GetString(3)),
                    Matches =
                        await GetMatchesAsync(
                            connection,
                            reader.GetInt64(0),
                            cancellationToken)
                });
        }

        return attempts;
    }
}
