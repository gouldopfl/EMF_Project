using System.Text;
using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class MedicalLiteratureClassificationService
{
    private readonly IIntelligenceCapabilityExecutor<
        TextStructuredExtractionRequest,
        string> _executor;

    private readonly MedicalLiteratureClassificationValidator
        _validator = new();

    public MedicalLiteratureClassificationService(
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public async Task<MedicalLiteratureClassificationResult>
        ClassifyAsync(
            MedicalLiteratureSource source,
            ArtifactId artifactId,
            string text,
            IReadOnlyList<Requirement> candidateRequirements,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(candidateRequirements);
        ArgumentNullException.ThrowIfNull(context);

        var result =
            await _executor.ExecuteAsync(
                IntelligenceCapabilityIds.TextStructuredExtraction,
                new TextStructuredExtractionRequest(
                    text,
                    BuildInstruction(source, candidateRequirements),
                    BuildJsonShape()),
                context,
                cancellationToken);

        if (!result.Success)
            return new MedicalLiteratureClassificationResult
            {
                IntelligenceResult = result
            };

        var extracted =
            JsonSerializer.Deserialize<ExtractedProposal>(
                result.Output!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidOperationException(
                "Structured literature classification returned no proposal.");

        var proposal =
            Map(source, artifactId, extracted);

        _validator.ValidateAgainstSource(
            proposal,
            source.Id,
            artifactId,
            candidateRequirements,
            text);

        return new MedicalLiteratureClassificationResult
        {
            IntelligenceResult = result,
            Proposal = proposal
        };
    }

    private static MedicalLiteratureClassificationProposal Map(
        MedicalLiteratureSource source,
        ArtifactId artifactId,
        ExtractedProposal extracted) =>
        new()
        {
            MedicalLiteratureSourceId = source.Id,
            ArtifactId = artifactId,
            Classifications =
                extracted.Classifications.Select(
                    item =>
                        new MedicalLiteratureRequirementClassification
                        {
                            RequirementId = new(item.RequirementId),
                            GuidanceRole = item.GuidanceRole,
                            Description = item.Description,
                            SourceExcerpts =
                                item.SourceExcerpts.Select(
                                    excerpt =>
                                        new MedicalLiteratureSourceExcerpt
                                        {
                                            ArtifactId = artifactId,
                                            Text = excerpt.Text,
                                            StartOffset = excerpt.StartOffset,
                                            Length = excerpt.Length
                                        }).ToArray()
                        }).ToArray()
        };

    private static string BuildInstruction(
        MedicalLiteratureSource source,
        IReadOnlyList<Requirement> requirements)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Classify the supplied medical literature only against " +
            "the candidate requirements listed below.");
        builder.AppendLine(
            "Do not invent requirements, evidence roles, facts, or quotations.");
        builder.AppendLine(
            "Return no classification when the article does not support " +
            "a candidate requirement.");
        builder.AppendLine(
            "Every classification must include exact supporting excerpts " +
            "with zero-based character offsets and lengths.");
        builder.AppendLine();
        builder.AppendLine($"Title: {source.Title}");
        builder.AppendLine($"Authors: {source.Authors}");
        builder.AppendLine($"Publication: {source.Publication}");
        builder.AppendLine();
        builder.AppendLine("Candidate requirements:");

        foreach (var requirement in requirements)
            builder.AppendLine(
                $"- {requirement.Id.Value}: {requirement.Description}");

        builder.AppendLine();
        builder.AppendLine(
            "Allowed guidance roles: SupportsRequirement, " +
            "EstablishesElement, Corroborates, Clarifies.");

        return builder.ToString();
    }

    private static string BuildJsonShape() =>
        """
        {
          "classifications": [{
            "requirementId": "string",
            "guidanceRole": "SupportsRequirement|EstablishesElement|Corroborates|Clarifies",
            "description": "string",
            "sourceExcerpts": [{
              "text": "string",
              "startOffset": 0,
              "length": 0
            }]
          }]
        }
        """;

    private sealed class ExtractedProposal
    {
        public required IReadOnlyList<ExtractedClassification>
            Classifications { get; init; }
    }

    private sealed class ExtractedClassification
    {
        public required string RequirementId { get; init; }
        public required string GuidanceRole { get; init; }
        public required string Description { get; init; }

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
