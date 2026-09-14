using System.Globalization;
using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.ConsoleApplication;

internal static class VeteransBoundedEvidenceReviewConsoleService
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public static async Task<int> ReviewAsync(
        string databasePath,
        string auditDatabasePath,
        ClaimIssueId claimIssueId,
        ServiceConnectionBasisId basisId,
        RequirementId requirementId,
        ArtifactId sourceArtifactId,
        string receiptPath,
        IArtifactContentStore? contentStore,
        string? reviewedBy,
        TextWriter output,
        string? supersedesCorrelationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            if (string.IsNullOrWhiteSpace(reviewedBy))
            {
                throw new InvalidOperationException(
                    "Bounded evidence promotion requires review. " +
                    "Set EMF_REVIEWED_BY to the reviewer identity.");
            }

            if (contentStore is null)
            {
                throw new InvalidOperationException(
                    "Artifact content store is not configured.");
            }

            if (!File.Exists(receiptPath))
            {
                throw new FileNotFoundException(
                    "Bounded evidence interpretation receipt was not found.",
                    receiptPath);
            }

            if (!File.Exists(auditDatabasePath))
            {
                throw new FileNotFoundException(
                    "Intelligence audit database was not found.",
                    auditDatabasePath);
            }

            var receipt =
                JsonSerializer.Deserialize<ReviewReceipt>(
                    await File.ReadAllTextAsync(
                        receiptPath,
                        cancellationToken),
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "Bounded evidence interpretation receipt is empty.");

            ValidateReceipt(receipt, requirementId);

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync(cancellationToken);

            var selections =
                await new VeteransBoundedEvidenceSelectionService(evidence)
                    .GetAsync(
                        claimIssueId,
                        basisId,
                        requirementId,
                        sourceArtifactId,
                        cancellationToken);

            var artifactId = new ArtifactId(receipt.ArtifactId!);
            var selection =
                selections.SingleOrDefault(
                    item => item.Artifact.Id == artifactId)
                ?? throw new InvalidOperationException(
                    $"Bounded evidence artifact '{artifactId.Value}' " +
                    "is not selected for the requested claim, basis, " +
                    "requirement, and source artifact.");

            var bytes =
                await contentStore.ReadAsync(
                    artifactId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Bounded evidence content '{artifactId.Value}' " +
                    "was not found.");

            var sourceText = StrictUtf8.GetString(bytes);
            ValidateExcerpts(receipt, artifactId, sourceText);

            var audit =
                await LoadAuditAsync(
                    auditDatabasePath,
                    receipt.CorrelationId!,
                    cancellationToken);

            var promotedUtc = DateTimeOffset.UtcNow;

            if (promotedUtc < audit.CompletedUtc)
            {
                throw new InvalidOperationException(
                    "Reviewed bounded evidence promotion predates the " +
                    "intelligence completion timestamp.");
            }

            var reviewed =
                new ReviewedBoundedEvidenceInterpretation
                {
                    ClaimIssueId = claimIssueId,
                    ServiceConnectionBasisId = basisId,
                    RequirementId = requirementId,
                    ArtifactId = artifactId,
                    Direction = receipt.Direction!,
                    OpinionStandard = receipt.OpinionStandard!,
                    MedicalConclusion = receipt.MedicalConclusion!,
                    RationaleSummary = receipt.RationaleSummary!,
                    PromotedBy = audit.SubjectId,
                    PromotedUtc = promotedUtc,
                    ReviewedBy = reviewedBy,
                    ReviewedUtc = promotedUtc,
                    CapabilityId = audit.CapabilityId,
                    ProviderId = audit.ProviderId,
                    CorrelationId = receipt.CorrelationId!,
                    EngineName = audit.EngineName,
                    EngineVersion = audit.EngineVersion,
                    ProviderOperationId = audit.ProviderOperationId,
                    InputTokenCount = audit.InputTokenCount,
                    OutputTokenCount = audit.OutputTokenCount,
                    TotalTokenCount = audit.TotalTokenCount,
                    EstimatedCostUsd = audit.EstimatedCostUsd,
                    StartedUtc = audit.StartedUtc,
                    CompletedUtc = audit.CompletedUtc,
                    RequiresReview = receipt.RequiresReview,
                    Warnings = receipt.Warnings ?? Array.Empty<string>(),
                    SourceExcerpts =
                        receipt.SourceExcerpts!
                            .Select(
                                excerpt =>
                                    new ReviewedBoundedEvidenceSourceExcerpt
                                    {
                                        ArtifactId = artifactId,
                                        Text = excerpt.Text!,
                                        StartOffset = excerpt.StartOffset,
                                        Length = excerpt.Length
                                    })
                            .ToArray()
                };

            var repository =
                new SqliteBoundedEvidenceInterpretationRepository(
                    databasePath);
            await repository.InitializeAsync(cancellationToken);

            if (supersedesCorrelationId is null)
            {
                await repository.AddReviewedInterpretationAsync(
                    reviewed,
                    cancellationToken);
            }
            else
            {
                await repository.SupersedeReviewedInterpretationsAsync(
                    supersedesCorrelationId,
                    [reviewed],
                    cancellationToken);
            }

            output.WriteLine("Mode                : LOCAL REVIEW/PROMOTE");
            output.WriteLine("Azure Intelligence  : NOT USED");
            output.WriteLine($"Artifact ID         : {artifactId.Value}");
            output.WriteLine($"Evidence Date       : {selection.EvidenceDate:yyyy-MM-dd}");
            output.WriteLine(
                "Evidence Title      : " +
                ConsoleTextSanitizer.Sanitize(selection.EvidenceTitle));
            output.WriteLine($"Requirement         : {requirementId.Value}");
            output.WriteLine($"Direction           : {reviewed.Direction}");
            output.WriteLine($"Opinion Standard    : {reviewed.OpinionStandard}");
            output.WriteLine(
                $"Correlation         : {reviewed.CorrelationId}");
            output.WriteLine(
                $"Reviewed By         : {ConsoleTextSanitizer.Sanitize(reviewedBy)}");
            output.WriteLine(
                $"Source Excerpts     : {reviewed.SourceExcerpts.Count}");
            if (supersedesCorrelationId is not null)
            {
                output.WriteLine(
                    "Supersedes          : " +
                    ConsoleTextSanitizer.Sanitize(
                        supersedesCorrelationId));
            }
            output.WriteLine("Status              : PROMOTED");

            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            FileNotFoundException or
            InvalidDataException or
            InvalidOperationException or
            JsonException)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(exception.Message));
            return 1;
        }
    }

    public static async Task<int> ListAsync(
        string databasePath,
        string filterKind,
        string filterValue,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterValue);
        ArgumentNullException.ThrowIfNull(output);

        var repository =
            new SqliteBoundedEvidenceInterpretationRepository(databasePath);
        await repository.InitializeAsync(cancellationToken);

        IReadOnlyList<ReviewedBoundedEvidenceInterpretation> reviewed =
            filterKind switch
            {
                "requirement" =>
                    await repository.GetReviewedInterpretationsAsync(
                        new RequirementId(filterValue),
                        cancellationToken),
                "artifact" =>
                    await repository.GetReviewedInterpretationsAsync(
                        new ArtifactId(filterValue),
                        cancellationToken),
                _ => throw new ArgumentException(
                    "Reviewed bounded evidence filter must be " +
                    "'requirement' or 'artifact'.",
                    nameof(filterKind))
            };

        output.WriteLine("Mode                : LOCAL REVIEWED LIST");
        output.WriteLine("Azure Intelligence  : NOT USED");
        output.WriteLine($"Filter              : {filterKind}={filterValue}");
        output.WriteLine($"Reviewed            : {reviewed.Count}");
        output.WriteLine();

        foreach (var item in reviewed)
        {
            output.WriteLine($"Artifact ID         : {item.ArtifactId.Value}");
            output.WriteLine($"Claim Issue         : {item.ClaimIssueId.Value}");
            output.WriteLine(
                $"Basis               : {item.ServiceConnectionBasisId.Value}");
            output.WriteLine($"Requirement         : {item.RequirementId.Value}");
            output.WriteLine($"Direction           : {item.Direction}");
            output.WriteLine($"Opinion Standard    : {item.OpinionStandard}");
            output.WriteLine(
                "Medical Conclusion  : " +
                ConsoleTextSanitizer.Sanitize(item.MedicalConclusion));
            output.WriteLine(
                "Rationale           : " +
                ConsoleTextSanitizer.Sanitize(item.RationaleSummary));
            output.WriteLine(
                $"Reviewed By         : {ConsoleTextSanitizer.Sanitize(item.ReviewedBy)}");
            output.WriteLine($"Reviewed UTC        : {item.ReviewedUtc:O}");
            output.WriteLine($"Correlation         : {item.CorrelationId}");
            output.WriteLine(
                $"Source Excerpts     : {item.SourceExcerpts.Count}");
            output.WriteLine();
        }

        return 0;
    }

    private static void ValidateReceipt(
        ReviewReceipt receipt,
        RequirementId requirementId)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (string.IsNullOrWhiteSpace(receipt.ArtifactId) ||
            string.IsNullOrWhiteSpace(receipt.CorrelationId) ||
            string.IsNullOrWhiteSpace(receipt.Direction) ||
            string.IsNullOrWhiteSpace(receipt.OpinionStandard) ||
            string.IsNullOrWhiteSpace(receipt.MedicalConclusion) ||
            string.IsNullOrWhiteSpace(receipt.RationaleSummary))
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation receipt is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(receipt.RequirementId) &&
            !string.Equals(
                receipt.RequirementId,
                requirementId.Value,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation receipt requirement " +
                "does not match the requested requirement.");
        }

        if (receipt.Direction is not (
                VeteransBoundedEvidenceDirections.SupportsRequirement or
                VeteransBoundedEvidenceDirections.OpposesRequirement or
                VeteransBoundedEvidenceDirections.Indeterminate))
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation receipt direction is invalid.");
        }

        if (receipt.OpinionStandard is not (
                VeteransMedicalOpinionStandards.AtLeastAsLikelyAsNot or
                VeteransMedicalOpinionStandards.LessLikelyThanNot or
                VeteransMedicalOpinionStandards.NoExplicitStandard or
                VeteransMedicalOpinionStandards.Other))
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation receipt opinion standard " +
                "is invalid.");
        }

        if (receipt.SourceExcerpts is null ||
            receipt.SourceExcerpts.Count == 0)
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation receipt requires at " +
                "least one source excerpt.");
        }
    }

    private static void ValidateExcerpts(
        ReviewReceipt receipt,
        ArtifactId artifactId,
        string sourceText)
    {
        foreach (var excerpt in receipt.SourceExcerpts!)
        {
            if (excerpt is null ||
                string.IsNullOrWhiteSpace(excerpt.Text) ||
                excerpt.StartOffset < 0 ||
                excerpt.Length <= 0 ||
                excerpt.Length != excerpt.Text.Length ||
                excerpt.StartOffset > sourceText.Length - excerpt.Length ||
                !sourceText.AsSpan(
                        excerpt.StartOffset,
                        excerpt.Length)
                    .SequenceEqual(excerpt.Text.AsSpan()))
            {
                throw new InvalidOperationException(
                    $"Reviewed excerpt does not match bounded evidence " +
                    $"artifact '{artifactId.Value}'.");
            }
        }
    }

    private static async Task<AuditMetadata> LoadAuditAsync(
        string auditDatabasePath,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var rows =
            await new SqliteSecurityAuditRecordReader(
                    auditDatabasePath)
                .FindByFactAsync(
                    "IntelligenceCapability.Execute",
                    "IntelligenceCapability",
                    "text.structured.extract",
                    "correlationId",
                    correlationId,
                    maxResults: 2,
                    cancellationToken: cancellationToken);

        if (rows.Count != 1)
        {
            throw new InvalidOperationException(
                rows.Count == 0
                    ? "No intelligence audit record matches the receipt correlation."
                    : "Multiple intelligence audit records match the receipt correlation.");
        }

        var row = rows[0];

        if (!string.Equals(row.Outcome, "Succeeded", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(row.Destination))
        {
            throw new InvalidOperationException(
                "The matching intelligence audit record is not a successful " +
                "provider execution.");
        }

        using var document = JsonDocument.Parse(row.FactsJson);
        var facts = document.RootElement;

        string Required(string propertyName)
        {
            if (!facts.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException(
                    $"Intelligence audit metadata '{propertyName}' is missing.");
            }

            return property.GetString()!;
        }

        string? Optional(string propertyName) =>
            facts.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString())
                ? property.GetString()
                : null;

        int? OptionalInt(string propertyName) =>
            int.TryParse(
                Optional(propertyName),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : null;

        decimal? OptionalDecimal(string propertyName) =>
            decimal.TryParse(
                Optional(propertyName),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : null;

        return new AuditMetadata
        {
            CapabilityId = row.ResourceId,
            SubjectId = row.SubjectId,
            ProviderId = row.Destination!,
            EngineName = Required("engineName"),
            EngineVersion = Optional("engineVersion"),
            ProviderOperationId = Optional("providerOperationId"),
            InputTokenCount = OptionalInt("inputTokenCount"),
            OutputTokenCount = OptionalInt("outputTokenCount"),
            TotalTokenCount = OptionalInt("totalTokenCount"),
            EstimatedCostUsd = OptionalDecimal("estimatedCostUsd"),
            StartedUtc = DateTimeOffset.Parse(
                Required("startedUtc"),
                CultureInfo.InvariantCulture),
            CompletedUtc = DateTimeOffset.Parse(
                Required("completedUtc"),
                CultureInfo.InvariantCulture)
        };
    }

    internal sealed class ReviewReceipt
    {
        public string? ArtifactId { get; init; }
        public string? RequirementId { get; init; }
        public string? CorrelationId { get; init; }
        public string? Direction { get; init; }
        public string? OpinionStandard { get; init; }
        public string? MedicalConclusion { get; init; }
        public string? RationaleSummary { get; init; }
        public bool RequiresReview { get; init; } = true;
        public IReadOnlyList<string>? Warnings { get; init; } = [];
        public IReadOnlyList<ReviewExcerpt>? SourceExcerpts { get; init; }
    }

    internal sealed class ReviewExcerpt
    {
        public string? Text { get; init; }
        public int StartOffset { get; init; }
        public int Length { get; init; }
    }

    private sealed class AuditMetadata
    {
        public required string CapabilityId { get; init; }
        public required string SubjectId { get; init; }
        public required string ProviderId { get; init; }
        public required string EngineName { get; init; }
        public string? EngineVersion { get; init; }
        public string? ProviderOperationId { get; init; }
        public int? InputTokenCount { get; init; }
        public int? OutputTokenCount { get; init; }
        public int? TotalTokenCount { get; init; }
        public decimal? EstimatedCostUsd { get; init; }
        public required DateTimeOffset StartedUtc { get; init; }
        public required DateTimeOffset CompletedUtc { get; init; }
    }

}
