using System.Globalization;
using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteBoundedEvidenceInterpretationRepository :
    IBoundedEvidenceInterpretationRepository
{
    private readonly string _databasePath;

    public SqliteBoundedEvidenceInterpretationRepository(
        string databasePath)
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

    public Task AddReviewedInterpretationAsync(
        ReviewedBoundedEvidenceInterpretation interpretation,
        CancellationToken cancellationToken = default) =>
        AddReviewedInterpretationsAsync(
            [interpretation],
            cancellationToken);

    public async Task AddReviewedInterpretationsAsync(
        IReadOnlyList<ReviewedBoundedEvidenceInterpretation>
            interpretations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interpretations);

        if (interpretations.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one reviewed bounded evidence interpretation " +
                "is required.");
        }

        foreach (var interpretation in interpretations)
            Validate(interpretation);

        var keys = interpretations.Select(LogicalKey).ToHashSet();

        if (keys.Count != interpretations.Count)
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence interpretations contain " +
                "duplicate logical decisions.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        foreach (var interpretation in interpretations)
        {
            if (await ActiveLogicalDecisionExistsAsync(
                    connection,
                    transaction,
                    interpretation,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "An active reviewed bounded evidence interpretation " +
                    "already exists. Explicit supersession is required.");
            }
        }

        foreach (var interpretation in interpretations)
        {
            await AddAsync(
                connection,
                transaction,
                interpretation,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SupersedeReviewedInterpretationsAsync(
        string supersededCorrelationId,
        IReadOnlyList<ReviewedBoundedEvidenceInterpretation> replacements,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            supersededCorrelationId);
        ArgumentNullException.ThrowIfNull(replacements);

        if (replacements.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one replacement reviewed bounded evidence " +
                "interpretation is required.");
        }

        foreach (var replacement in replacements)
            Validate(replacement);

        var replacementCorrelationId = replacements[0].CorrelationId;
        var supersededUtc = replacements[0].PromotedUtc;

        if (string.Equals(
                supersededCorrelationId,
                replacementCorrelationId,
                StringComparison.Ordinal) ||
            replacements.Any(
                replacement =>
                    !string.Equals(
                        replacement.CorrelationId,
                        replacementCorrelationId,
                        StringComparison.Ordinal) ||
                    replacement.PromotedUtc != supersededUtc))
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence supersession must replace one " +
                "prior correlation with one coherent replacement batch.");
        }

        var replacementKeys = replacements
            .Select(LogicalKey)
            .ToHashSet();

        if (replacementKeys.Count != replacements.Count)
        {
            throw new InvalidOperationException(
                "Replacement reviewed bounded evidence interpretations " +
                "contain duplicate logical decisions.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        var supersededKeys = new HashSet<ReviewedLogicalKey>();

        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT ClaimIssueId, ServiceConnectionBasisId,
                       RequirementId, ArtifactId
                FROM VeteransClaims_ReviewedBoundedEvidenceInterpretations
                WHERE CorrelationId = $correlation
                  AND SupersededUtc IS NULL;
                """;
            lookup.Parameters.AddWithValue(
                "$correlation",
                supersededCorrelationId);

            await using var reader =
                await lookup.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                supersededKeys.Add(
                    new ReviewedLogicalKey(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3)));
            }
        }

        if (supersededKeys.Count == 0)
        {
            throw new InvalidOperationException(
                "The reviewed bounded evidence correlation to supersede " +
                "is not active.");
        }

        if (!supersededKeys.SetEquals(replacementKeys))
        {
            throw new InvalidOperationException(
                "Replacement reviewed bounded evidence interpretations " +
                "must exactly match the active logical decisions being " +
                "superseded.");
        }

        await using (var supersede = connection.CreateCommand())
        {
            supersede.Transaction = transaction;
            supersede.CommandText = """
                UPDATE VeteransClaims_ReviewedBoundedEvidenceInterpretations
                SET SupersededByCorrelationId = $replacement,
                    SupersededUtc = $supersededUtc
                WHERE CorrelationId = $superseded
                  AND SupersededUtc IS NULL;
                """;
            supersede.Parameters.AddWithValue(
                "$replacement",
                replacementCorrelationId);
            supersede.Parameters.AddWithValue(
                "$supersededUtc",
                supersededUtc.ToString("O"));
            supersede.Parameters.AddWithValue(
                "$superseded",
                supersededCorrelationId);

            if (await supersede.ExecuteNonQueryAsync(cancellationToken) !=
                supersededKeys.Count)
            {
                throw new InvalidOperationException(
                    "The active reviewed bounded evidence supersession set " +
                    "changed unexpectedly.");
            }
        }

        foreach (var replacement in replacements)
        {
            await AddAsync(
                connection,
                transaction,
                replacement,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ReviewedBoundedEvidenceInterpretation>>
        GetReviewedInterpretationsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        GetReviewedInterpretationsAsync(
            requirementId: requirementId,
            artifactId: null,
            cancellationToken: cancellationToken);

    public Task<IReadOnlyList<ReviewedBoundedEvidenceInterpretation>>
        GetReviewedInterpretationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        GetReviewedInterpretationsAsync(
            requirementId: null,
            artifactId: artifactId,
            cancellationToken: cancellationToken);

    private async Task<IReadOnlyList<ReviewedBoundedEvidenceInterpretation>>
        GetReviewedInterpretationsAsync(
            RequirementId? requirementId,
            ArtifactId? artifactId,
            CancellationToken cancellationToken)
    {
        if ((requirementId is null) == (artifactId is null))
        {
            throw new ArgumentException(
                "Exactly one reviewed bounded evidence filter is required.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<ReviewedRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ClaimIssueId, ServiceConnectionBasisId,
                       RequirementId, ArtifactId, Direction,
                       OpinionStandard, MedicalConclusion, RationaleSummary,
                       PromotedBy, PromotedUtc, ReviewedBy, ReviewedUtc,
                       CapabilityId, ProviderId, CorrelationId, EngineName,
                       EngineVersion, ProviderOperationId,
                       InputTokenCount, OutputTokenCount, TotalTokenCount,
                       EstimatedCostUsd, StartedUtc, CompletedUtc,
                       RequiresReview, WarningsJson
                FROM VeteransClaims_ReviewedBoundedEvidenceInterpretations
                WHERE SupersededUtc IS NULL
                """ +
                (requirementId is not null
                    ? " AND RequirementId = $filter"
                    : " AND ArtifactId = $filter") +
                " ORDER BY ClaimIssueId, ServiceConnectionBasisId, " +
                "RequirementId, ArtifactId, CorrelationId;";

            command.Parameters.AddWithValue(
                "$filter",
                requirementId?.Value ?? artifactId!.Value.Value);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                rows.Add(ReadRow(reader));
        }

        var results =
            new List<ReviewedBoundedEvidenceInterpretation>(rows.Count);

        foreach (var row in rows)
        {
            var excerpts =
                new List<ReviewedBoundedEvidenceSourceExcerpt>();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ArtifactId, Text, StartOffset, Length
                FROM VeteransClaims_ReviewedBoundedEvidenceExcerpts
                WHERE ClaimIssueId = $claimIssue
                  AND ServiceConnectionBasisId = $basis
                  AND RequirementId = $requirement
                  AND ArtifactId = $artifact
                  AND CorrelationId = $correlation
                ORDER BY ExcerptOrdinal;
                """;
            command.Parameters.AddWithValue(
                "$claimIssue",
                row.ClaimIssueId.Value);
            command.Parameters.AddWithValue(
                "$basis",
                row.ServiceConnectionBasisId.Value);
            command.Parameters.AddWithValue(
                "$requirement",
                row.RequirementId.Value);
            command.Parameters.AddWithValue(
                "$artifact",
                row.ArtifactId.Value);
            command.Parameters.AddWithValue(
                "$correlation",
                row.CorrelationId);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                excerpts.Add(
                    new ReviewedBoundedEvidenceSourceExcerpt
                    {
                        ArtifactId = new ArtifactId(reader.GetString(0)),
                        Text = reader.GetString(1),
                        StartOffset = reader.GetInt32(2),
                        Length = reader.GetInt32(3)
                    });
            }

            results.Add(
                new ReviewedBoundedEvidenceInterpretation
                {
                    ClaimIssueId = row.ClaimIssueId,
                    ServiceConnectionBasisId = row.ServiceConnectionBasisId,
                    RequirementId = row.RequirementId,
                    ArtifactId = row.ArtifactId,
                    Direction = row.Direction,
                    OpinionStandard = row.OpinionStandard,
                    MedicalConclusion = row.MedicalConclusion,
                    RationaleSummary = row.RationaleSummary,
                    PromotedBy = row.PromotedBy,
                    PromotedUtc = row.PromotedUtc,
                    ReviewedBy = row.ReviewedBy,
                    ReviewedUtc = row.ReviewedUtc,
                    CapabilityId = row.CapabilityId,
                    ProviderId = row.ProviderId,
                    CorrelationId = row.CorrelationId,
                    EngineName = row.EngineName,
                    EngineVersion = row.EngineVersion,
                    ProviderOperationId = row.ProviderOperationId,
                    InputTokenCount = row.InputTokenCount,
                    OutputTokenCount = row.OutputTokenCount,
                    TotalTokenCount = row.TotalTokenCount,
                    EstimatedCostUsd = row.EstimatedCostUsd,
                    StartedUtc = row.StartedUtc,
                    CompletedUtc = row.CompletedUtc,
                    RequiresReview = row.RequiresReview,
                    Warnings = row.Warnings,
                    SourceExcerpts = excerpts
                });
        }

        return results;
    }

    private static async Task<bool> ActiveLogicalDecisionExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ReviewedBoundedEvidenceInterpretation interpretation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM VeteransClaims_ReviewedBoundedEvidenceInterpretations
            WHERE ClaimIssueId = $claimIssue
              AND ServiceConnectionBasisId = $basis
              AND RequirementId = $requirement
              AND ArtifactId = $artifact
              AND SupersededUtc IS NULL;
            """;
        AddLogicalKeyParameters(command, interpretation);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task AddAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ReviewedBoundedEvidenceInterpretation interpretation,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO
                    VeteransClaims_ReviewedBoundedEvidenceInterpretations
                (ClaimIssueId, ServiceConnectionBasisId, RequirementId,
                 ArtifactId, Direction, OpinionStandard, MedicalConclusion,
                 RationaleSummary, PromotedBy, PromotedUtc,
                 ReviewedBy, ReviewedUtc, CapabilityId, ProviderId,
                 CorrelationId, EngineName, EngineVersion,
                 ProviderOperationId, InputTokenCount, OutputTokenCount,
                 TotalTokenCount, EstimatedCostUsd, StartedUtc, CompletedUtc,
                 RequiresReview, WarningsJson,
                 SupersededByCorrelationId, SupersededUtc)
                VALUES
                ($claimIssue, $basis, $requirement,
                 $artifact, $direction, $opinionStandard, $medicalConclusion,
                 $rationaleSummary, $promotedBy, $promotedUtc,
                 $reviewedBy, $reviewedUtc, $capabilityId, $providerId,
                 $correlationId, $engineName, $engineVersion,
                 $providerOperationId, $inputTokens, $outputTokens,
                 $totalTokens, $estimatedCost, $startedUtc, $completedUtc,
                 $requiresReview, $warnings, NULL, NULL);
                """;
            AddLogicalKeyParameters(command, interpretation);
            command.Parameters.AddWithValue(
                "$direction",
                interpretation.Direction);
            command.Parameters.AddWithValue(
                "$opinionStandard",
                interpretation.OpinionStandard);
            command.Parameters.AddWithValue(
                "$medicalConclusion",
                interpretation.MedicalConclusion);
            command.Parameters.AddWithValue(
                "$rationaleSummary",
                interpretation.RationaleSummary);
            command.Parameters.AddWithValue(
                "$promotedBy",
                interpretation.PromotedBy);
            command.Parameters.AddWithValue(
                "$promotedUtc",
                interpretation.PromotedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$reviewedBy",
                interpretation.ReviewedBy);
            command.Parameters.AddWithValue(
                "$reviewedUtc",
                interpretation.ReviewedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$capabilityId",
                interpretation.CapabilityId);
            command.Parameters.AddWithValue(
                "$providerId",
                interpretation.ProviderId);
            command.Parameters.AddWithValue(
                "$correlationId",
                interpretation.CorrelationId);
            command.Parameters.AddWithValue(
                "$engineName",
                interpretation.EngineName);
            command.Parameters.AddWithValue(
                "$engineVersion",
                (object?)interpretation.EngineVersion ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$providerOperationId",
                (object?)interpretation.ProviderOperationId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$inputTokens",
                (object?)interpretation.InputTokenCount ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$outputTokens",
                (object?)interpretation.OutputTokenCount ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$totalTokens",
                (object?)interpretation.TotalTokenCount ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$estimatedCost",
                interpretation.EstimatedCostUsd.HasValue
                    ? interpretation.EstimatedCostUsd.Value.ToString(
                        CultureInfo.InvariantCulture)
                    : DBNull.Value);
            command.Parameters.AddWithValue(
                "$startedUtc",
                interpretation.StartedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$completedUtc",
                interpretation.CompletedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$requiresReview",
                interpretation.RequiresReview ? 1 : 0);
            command.Parameters.AddWithValue(
                "$warnings",
                JsonSerializer.Serialize(interpretation.Warnings));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0;
             index < interpretation.SourceExcerpts.Count;
             index++)
        {
            var excerpt = interpretation.SourceExcerpts[index];

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO VeteransClaims_ReviewedBoundedEvidenceExcerpts
                (ClaimIssueId, ServiceConnectionBasisId, RequirementId,
                 ArtifactId, CorrelationId, ExcerptOrdinal,
                 Text, StartOffset, Length)
                VALUES
                ($claimIssue, $basis, $requirement,
                 $artifact, $correlationId, $ordinal,
                 $text, $startOffset, $length);
                """;
            AddLogicalKeyParameters(command, interpretation);
            command.Parameters.AddWithValue(
                "$correlationId",
                interpretation.CorrelationId);
            command.Parameters.AddWithValue("$ordinal", index);
            command.Parameters.AddWithValue("$text", excerpt.Text);
            command.Parameters.AddWithValue(
                "$startOffset",
                excerpt.StartOffset);
            command.Parameters.AddWithValue("$length", excerpt.Length);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddLogicalKeyParameters(
        SqliteCommand command,
        ReviewedBoundedEvidenceInterpretation interpretation)
    {
        command.Parameters.AddWithValue(
            "$claimIssue",
            interpretation.ClaimIssueId.Value);
        command.Parameters.AddWithValue(
            "$basis",
            interpretation.ServiceConnectionBasisId.Value);
        command.Parameters.AddWithValue(
            "$requirement",
            interpretation.RequirementId.Value);
        command.Parameters.AddWithValue(
            "$artifact",
            interpretation.ArtifactId.Value);
    }

    private static ReviewedLogicalKey LogicalKey(
        ReviewedBoundedEvidenceInterpretation interpretation) =>
        new(
            interpretation.ClaimIssueId.Value,
            interpretation.ServiceConnectionBasisId.Value,
            interpretation.RequirementId.Value,
            interpretation.ArtifactId.Value);

    private static void Validate(
        ReviewedBoundedEvidenceInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        ArgumentNullException.ThrowIfNull(interpretation.Warnings);
        ArgumentNullException.ThrowIfNull(interpretation.SourceExcerpts);

        if (string.IsNullOrWhiteSpace(interpretation.Direction) ||
            string.IsNullOrWhiteSpace(interpretation.OpinionStandard) ||
            string.IsNullOrWhiteSpace(interpretation.MedicalConclusion) ||
            string.IsNullOrWhiteSpace(interpretation.RationaleSummary))
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence interpretation content is incomplete.");
        }

        if (string.IsNullOrWhiteSpace(interpretation.PromotedBy) ||
            string.IsNullOrWhiteSpace(interpretation.ReviewedBy) ||
            string.IsNullOrWhiteSpace(interpretation.CapabilityId) ||
            string.IsNullOrWhiteSpace(interpretation.ProviderId) ||
            string.IsNullOrWhiteSpace(interpretation.CorrelationId) ||
            string.IsNullOrWhiteSpace(interpretation.EngineName))
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence promotion provenance is incomplete.");
        }

        if (interpretation.StartedUtc == default ||
            interpretation.CompletedUtc == default ||
            interpretation.ReviewedUtc == default ||
            interpretation.PromotedUtc == default ||
            interpretation.CompletedUtc < interpretation.StartedUtc ||
            interpretation.ReviewedUtc < interpretation.CompletedUtc ||
            interpretation.PromotedUtc < interpretation.ReviewedUtc)
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence promotion timestamps are invalid.");
        }

        if (interpretation.InputTokenCount < 0 ||
            interpretation.OutputTokenCount < 0 ||
            interpretation.TotalTokenCount < 0 ||
            interpretation.EstimatedCostUsd < 0)
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence telemetry is invalid.");
        }

        if (interpretation.InputTokenCount.HasValue &&
            interpretation.OutputTokenCount.HasValue &&
            interpretation.TotalTokenCount.HasValue &&
            interpretation.TotalTokenCount.Value !=
                interpretation.InputTokenCount.Value +
                interpretation.OutputTokenCount.Value)
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence token telemetry is inconsistent.");
        }

        if (interpretation.SourceExcerpts.Count == 0)
        {
            throw new InvalidOperationException(
                "Reviewed bounded evidence requires at least one accepted excerpt.");
        }

        foreach (var excerpt in interpretation.SourceExcerpts)
        {
            ArgumentNullException.ThrowIfNull(excerpt);

            if (excerpt.ArtifactId != interpretation.ArtifactId ||
                string.IsNullOrWhiteSpace(excerpt.Text) ||
                excerpt.StartOffset < 0 ||
                excerpt.Length <= 0 ||
                excerpt.Length != excerpt.Text.Length)
            {
                throw new InvalidOperationException(
                    "Reviewed bounded evidence excerpt provenance is invalid.");
            }
        }
    }

    private static ReviewedRow ReadRow(SqliteDataReader reader) =>
        new()
        {
            ClaimIssueId = new ClaimIssueId(reader.GetString(0)),
            ServiceConnectionBasisId =
                new ServiceConnectionBasisId(reader.GetString(1)),
            RequirementId = new RequirementId(reader.GetString(2)),
            ArtifactId = new ArtifactId(reader.GetString(3)),
            Direction = reader.GetString(4),
            OpinionStandard = reader.GetString(5),
            MedicalConclusion = reader.GetString(6),
            RationaleSummary = reader.GetString(7),
            PromotedBy = reader.GetString(8),
            PromotedUtc = DateTimeOffset.Parse(reader.GetString(9)),
            ReviewedBy = reader.GetString(10),
            ReviewedUtc = DateTimeOffset.Parse(reader.GetString(11)),
            CapabilityId = reader.GetString(12),
            ProviderId = reader.GetString(13),
            CorrelationId = reader.GetString(14),
            EngineName = reader.GetString(15),
            EngineVersion = reader.IsDBNull(16) ? null : reader.GetString(16),
            ProviderOperationId =
                reader.IsDBNull(17) ? null : reader.GetString(17),
            InputTokenCount =
                reader.IsDBNull(18) ? null : reader.GetInt32(18),
            OutputTokenCount =
                reader.IsDBNull(19) ? null : reader.GetInt32(19),
            TotalTokenCount =
                reader.IsDBNull(20) ? null : reader.GetInt32(20),
            EstimatedCostUsd =
                reader.IsDBNull(21)
                    ? null
                    : decimal.Parse(
                        reader.GetString(21),
                        CultureInfo.InvariantCulture),
            StartedUtc = DateTimeOffset.Parse(reader.GetString(22)),
            CompletedUtc = DateTimeOffset.Parse(reader.GetString(23)),
            RequiresReview = reader.GetInt32(24) != 0,
            Warnings =
                JsonSerializer.Deserialize<string[]>(reader.GetString(25))
                ?? Array.Empty<string>()
        };

    private readonly record struct ReviewedLogicalKey(
        string ClaimIssueId,
        string ServiceConnectionBasisId,
        string RequirementId,
        string ArtifactId);

    private sealed class ReviewedRow
    {
        public required ClaimIssueId ClaimIssueId { get; init; }
        public required ServiceConnectionBasisId ServiceConnectionBasisId
        { get; init; }
        public required RequirementId RequirementId { get; init; }
        public required ArtifactId ArtifactId { get; init; }
        public required string Direction { get; init; }
        public required string OpinionStandard { get; init; }
        public required string MedicalConclusion { get; init; }
        public required string RationaleSummary { get; init; }
        public required string PromotedBy { get; init; }
        public required DateTimeOffset PromotedUtc { get; init; }
        public required string ReviewedBy { get; init; }
        public required DateTimeOffset ReviewedUtc { get; init; }
        public required string CapabilityId { get; init; }
        public required string ProviderId { get; init; }
        public required string CorrelationId { get; init; }
        public required string EngineName { get; init; }
        public string? EngineVersion { get; init; }
        public string? ProviderOperationId { get; init; }
        public int? InputTokenCount { get; init; }
        public int? OutputTokenCount { get; init; }
        public int? TotalTokenCount { get; init; }
        public decimal? EstimatedCostUsd { get; init; }
        public required DateTimeOffset StartedUtc { get; init; }
        public required DateTimeOffset CompletedUtc { get; init; }
        public required bool RequiresReview { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
    }
}
