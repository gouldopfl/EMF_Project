using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransBoundedEvidenceDirections
{
    public const string SupportsRequirement = "SupportsRequirement";
    public const string OpposesRequirement = "OpposesRequirement";
    public const string Indeterminate = "Indeterminate";
}

public static class VeteransMedicalOpinionStandards
{
    public const string AtLeastAsLikelyAsNot = "AtLeastAsLikelyAsNot";
    public const string LessLikelyThanNot = "LessLikelyThanNot";
    public const string NoExplicitStandard = "NoExplicitStandard";
    public const string Other = "Other";
}

public sealed class VeteransBoundedEvidenceSourceExcerpt
{
    public required ArtifactId ArtifactId { get; init; }

    public required string Text { get; init; }

    public int? StartOffset { get; init; }

    public int? Length { get; init; }
}

public sealed class VeteransBoundedEvidenceInterpretation
{
    public required ArtifactId ArtifactId { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required string Direction { get; init; }

    public required string OpinionStandard { get; init; }

    public required string MedicalConclusion { get; init; }

    public required string RationaleSummary { get; init; }

    public required IReadOnlyList<VeteransBoundedEvidenceSourceExcerpt>
        SourceExcerpts { get; init; }
}

public sealed class VeteransBoundedEvidenceInterpretationResult
{
    public required VeteransBoundedEvidenceSelection Evidence { get; init; }

    public required IntelligenceCapabilityResult<string>
        IntelligenceResult { get; init; }

    public VeteransBoundedEvidenceInterpretation? Interpretation
    { get; init; }
}

public sealed class VeteransBoundedEvidenceInterpretationService
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    private readonly VeteransBoundedEvidenceSelectionService _selection;
    private readonly IArtifactContentStore _contentStore;
    private readonly IIntelligenceCapabilityExecutor<
        TextStructuredExtractionRequest,
        string> _executor;

    public VeteransBoundedEvidenceInterpretationService(
        VeteransBoundedEvidenceSelectionService selection,
        IArtifactContentStore contentStore,
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(executor);

        _selection = selection;
        _contentStore = contentStore;
        _executor = executor;
    }

    public async Task<IReadOnlyList<
        VeteransBoundedEvidenceInterpretationResult>> InterpretAsync(
            ClaimIssueId claimIssueId,
            ServiceConnectionBasisId basisId,
            Requirement requirement,
            ArtifactId sourceArtifactId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(context);

        var evidence =
            await _selection.GetAsync(
                claimIssueId,
                basisId,
                requirement.Id,
                sourceArtifactId,
                cancellationToken);

        if (evidence.Count == 0)
            return Array.Empty<
                VeteransBoundedEvidenceInterpretationResult>();

        var results =
            new List<VeteransBoundedEvidenceInterpretationResult>(
                evidence.Count);

        foreach (var item in evidence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content =
                await _contentStore.ReadAsync(
                    item.Artifact.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Bounded evidence content '{item.Artifact.Id.Value}' " +
                    "was not found.");

            if (content.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Bounded evidence content '{item.Artifact.Id.Value}' " +
                    "is empty.");
            }

            string text;

            try
            {
                text = StrictUtf8.GetString(content);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Bounded evidence content is not valid UTF-8.",
                    exception);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    $"Bounded evidence content '{item.Artifact.Id.Value}' " +
                    "contains no text.");
            }

            var inputArtifactIds =
                context.InputArtifactIds
                    .Append(item.Artifact.Id)
                    .Distinct()
                    .ToArray();

            var itemContext =
                new IntelligenceExecutionContext(
                    context.SubjectId,
                    context.CorrelationId,
                    context.ProtectionClassificationId,
                    inputArtifactIds,
                    context.AgentId);

            var intelligenceResult =
                await _executor.ExecuteAsync(
                    IntelligenceCapabilityIds.TextStructuredExtraction,
                    new TextStructuredExtractionRequest(
                        text,
                        BuildInstruction(requirement, item),
                        BuildJsonShape()),
                    itemContext,
                    cancellationToken);

            if (!intelligenceResult.Success)
            {
                results.Add(
                    new VeteransBoundedEvidenceInterpretationResult
                    {
                        Evidence = item,
                        IntelligenceResult = intelligenceResult
                    });

                continue;
            }

            var extracted =
                JsonSerializer.Deserialize<ExtractedInterpretation>(
                    intelligenceResult.Output!,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? throw new InvalidOperationException(
                    "Structured bounded-evidence interpretation " +
                    "returned no interpretation.");

            var interpretation =
                Map(item.Artifact.Id, extracted);

            Validate(
                interpretation,
                item.Artifact.Id,
                requirement.Id,
                text);

            results.Add(
                new VeteransBoundedEvidenceInterpretationResult
                {
                    Evidence = item,
                    IntelligenceResult = intelligenceResult,
                    Interpretation = interpretation
                });
        }

        return results;
    }

    private static VeteransBoundedEvidenceInterpretation Map(
        ArtifactId artifactId,
        ExtractedInterpretation extracted) =>
        new()
        {
            ArtifactId = artifactId,
            RequirementId = new RequirementId(extracted.RequirementId),
            Direction = extracted.Direction,
            OpinionStandard = extracted.OpinionStandard,
            MedicalConclusion = extracted.MedicalConclusion,
            RationaleSummary = extracted.RationaleSummary,
            SourceExcerpts =
                extracted.SourceExcerpts.Select(
                    excerpt =>
                        new VeteransBoundedEvidenceSourceExcerpt
                        {
                            ArtifactId = artifactId,
                            Text = excerpt.Text,
                            StartOffset = excerpt.StartOffset,
                            Length = excerpt.Length
                        }).ToArray()
        };

    private static void Validate(
        VeteransBoundedEvidenceInterpretation interpretation,
        ArtifactId artifactId,
        RequirementId requirementId,
        string sourceText)
    {
        if (interpretation.ArtifactId != artifactId)
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation artifact identity mismatch.");
        }

        if (interpretation.RequirementId != requirementId)
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation requirement mismatch.");
        }

        if (interpretation.Direction is not (
            VeteransBoundedEvidenceDirections.SupportsRequirement or
            VeteransBoundedEvidenceDirections.OpposesRequirement or
            VeteransBoundedEvidenceDirections.Indeterminate))
        {
            throw new InvalidOperationException(
                $"Unsupported bounded evidence direction " +
                $"'{interpretation.Direction}'.");
        }

        if (interpretation.OpinionStandard is not (
            VeteransMedicalOpinionStandards.AtLeastAsLikelyAsNot or
            VeteransMedicalOpinionStandards.LessLikelyThanNot or
            VeteransMedicalOpinionStandards.NoExplicitStandard or
            VeteransMedicalOpinionStandards.Other))
        {
            throw new InvalidOperationException(
                $"Unsupported medical opinion standard " +
                $"'{interpretation.OpinionStandard}'.");
        }

        if (string.IsNullOrWhiteSpace(
                interpretation.MedicalConclusion))
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation must contain " +
                "a medical conclusion.");
        }

        if (string.IsNullOrWhiteSpace(
                interpretation.RationaleSummary))
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation must contain " +
                "a rationale summary.");
        }

        if (interpretation.SourceExcerpts.Count == 0)
        {
            throw new InvalidOperationException(
                "Bounded evidence interpretation must contain " +
                "at least one source excerpt.");
        }

        foreach (var excerpt in interpretation.SourceExcerpts)
        {
            ValidateExcerpt(
                excerpt,
                artifactId,
                sourceText);
        }
    }

    private static void ValidateExcerpt(
        VeteransBoundedEvidenceSourceExcerpt excerpt,
        ArtifactId artifactId,
        string sourceText)
    {
        ArgumentNullException.ThrowIfNull(excerpt);

        if (excerpt.ArtifactId != artifactId)
        {
            throw new InvalidOperationException(
                "A bounded evidence source excerpt must reference " +
                "the interpreted artifact.");
        }

        if (string.IsNullOrWhiteSpace(excerpt.Text))
        {
            throw new InvalidOperationException(
                "A bounded evidence source excerpt cannot be empty.");
        }

        if (excerpt.StartOffset is null || excerpt.Length is null)
        {
            throw new InvalidOperationException(
                "A bounded evidence source excerpt must contain " +
                "a start offset and length.");
        }

        var start = excerpt.StartOffset.Value;
        var length = excerpt.Length.Value;

        if (start < 0 ||
            length < 0 ||
            length != excerpt.Text.Length ||
            length > sourceText.Length ||
            start > sourceText.Length - length)
        {
            throw new InvalidOperationException(
                "A bounded evidence source excerpt range is invalid.");
        }

        if (!sourceText.AsSpan(start, length)
                .SequenceEqual(excerpt.Text.AsSpan()))
        {
            throw new InvalidOperationException(
                "A bounded evidence source excerpt does not match " +
                "the bounded evidence text.");
        }
    }

    private static string BuildInstruction(
        Requirement requirement,
        VeteransBoundedEvidenceSelection evidence) =>
        $"""
        Interpret only the supplied bounded medical-opinion text against
        the single candidate requirement below.

        Candidate requirement ID: {requirement.Id.Value}
        Candidate requirement: {requirement.Description}
        Evidence title: {evidence.EvidenceTitle}
        Evidence date: {evidence.EvidenceDate:yyyy-MM-dd}
        Source pages: {evidence.SourceStartPage}-{evidence.SourceEndPage}

        Direction is relative to the candidate requirement:
        - SupportsRequirement: the medical opinion supports satisfying it.
        - OpposesRequirement: the medical opinion explicitly weighs against it.
        - Indeterminate: the medical opinion does not clearly resolve it.

        Allowed opinion standards are AtLeastAsLikelyAsNot,
        LessLikelyThanNot, NoExplicitStandard, or Other.

        State the medical conclusion and summarize the stated rationale.
        Use only facts and reasoning present in the bounded source text.
        Do not invent diagnoses, relationships, medical mechanisms, or facts.
        Include at least one exact source excerpt supporting the interpretation.
        Every source excerpt must include its zero-based character startOffset
        and exact character length in the supplied bounded text.
        """;

    private static string BuildJsonShape() =>
        """
        {
          "requirementId": "string",
          "direction": "SupportsRequirement|OpposesRequirement|Indeterminate",
          "opinionStandard": "AtLeastAsLikelyAsNot|LessLikelyThanNot|NoExplicitStandard|Other",
          "medicalConclusion": "string",
          "rationaleSummary": "string",
          "sourceExcerpts": [{
            "text": "string",
            "startOffset": 0,
            "length": 0
          }]
        }
        """;

    private sealed class ExtractedInterpretation
    {
        public required string RequirementId { get; init; }

        public required string Direction { get; init; }

        public required string OpinionStandard { get; init; }

        public required string MedicalConclusion { get; init; }

        public required string RationaleSummary { get; init; }

        public required IReadOnlyList<ExtractedExcerpt>
            SourceExcerpts { get; init; }
    }

    private sealed class ExtractedExcerpt
    {
        public required string Text { get; init; }

        public int? StartOffset { get; init; }

        public int? Length { get; init; }
    }
}
