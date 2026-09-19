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
    private const int MaxGroundingSegmentLength = 2000;

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

        var groundingSegments =
            BuildGroundingSegments(text);

        var segmentById =
            groundingSegments.ToDictionary(
                segment => segment.Id,
                StringComparer.Ordinal);

        var result =
            await _executor.ExecuteAsync(
                IntelligenceCapabilityIds.TextStructuredExtraction,
                new TextStructuredExtractionRequest(
                    BuildGroundedInput(groundingSegments),
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
            Map(source, artifactId, extracted, segmentById);

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
        ExtractedProposal extracted,
        IReadOnlyDictionary<string, GroundingSegment> segmentById) =>
        new()
        {
            MedicalLiteratureSourceId = source.Id,
            ArtifactId = artifactId,
            Classifications =
                extracted.Classifications
                    .GroupBy(
                        item =>
                            (item.RequirementId, item.GuidanceRole))
                    .Select(
                        group =>
                        {
                            var first = group.First();

                            return new MedicalLiteratureRequirementClassification
                            {
                                RequirementId = new(first.RequirementId),
                                GuidanceRole = first.GuidanceRole,
                                Description = first.Description,
                                SourceExcerpts =
                                    group
                                        .SelectMany(
                                            item => item.SourceSegmentIds)
                                        .Distinct(StringComparer.Ordinal)
                                        .Select(
                                            segmentId =>
                                                MapSegment(
                                                    artifactId,
                                                    segmentId,
                                                    segmentById))
                                        .ToArray()
                            };
                        })
                    .ToArray()
        };

    private static MedicalLiteratureSourceExcerpt MapSegment(
        ArtifactId artifactId,
        string segmentId,
        IReadOnlyDictionary<string, GroundingSegment> segmentById)
    {
        if (string.IsNullOrWhiteSpace(segmentId) ||
            !segmentById.TryGetValue(segmentId, out var segment))
        {
            throw new InvalidOperationException(
                $"Medical literature source segment '{segmentId}' " +
                "does not exist in the source document.");
        }

        return new MedicalLiteratureSourceExcerpt
        {
            ArtifactId = artifactId,
            Text = segment.Text,
            StartOffset = segment.StartOffset,
            Length = segment.Length
        };
    }

    private static IReadOnlyList<GroundingSegment>
        BuildGroundingSegments(string sourceText)
    {
        var segments = new List<GroundingSegment>();
        var lineStart = 0;

        for (var index = 0; index <= sourceText.Length; index++)
        {
            var atEnd = index == sourceText.Length;
            var isLineBreak =
                !atEnd &&
                (sourceText[index] == '\r' ||
                 sourceText[index] == '\n');

            if (!atEnd && !isLineBreak)
                continue;

            AddGroundingSegment(
                sourceText,
                lineStart,
                index,
                segments);

            if (!atEnd &&
                sourceText[index] == '\r' &&
                index + 1 < sourceText.Length &&
                sourceText[index + 1] == '\n')
            {
                index++;
            }

            lineStart = index + 1;
        }

        if (segments.Count == 0)
        {
            throw new InvalidOperationException(
                "Medical literature content contains no grounding segments.");
        }

        return segments;
    }

    private static void AddGroundingSegment(
        string sourceText,
        int start,
        int endExclusive,
        ICollection<GroundingSegment> segments)
    {
        while (start < endExclusive &&
               char.IsWhiteSpace(sourceText[start]))
        {
            start++;
        }

        while (endExclusive > start &&
               char.IsWhiteSpace(sourceText[endExclusive - 1]))
        {
            endExclusive--;
        }

        if (start >= endExclusive)
            return;

        while (start < endExclusive)
        {
            var segmentEnd = Math.Min(
                start + MaxGroundingSegmentLength,
                endExclusive);

            if (segmentEnd < endExclusive)
            {
                var preferredBreak = segmentEnd;
                while (preferredBreak > start &&
                       !char.IsWhiteSpace(sourceText[preferredBreak - 1]))
                {
                    preferredBreak--;
                }

                if (preferredBreak > start)
                    segmentEnd = preferredBreak;
            }

            while (segmentEnd > start &&
                   char.IsWhiteSpace(sourceText[segmentEnd - 1]))
            {
                segmentEnd--;
            }

            if (segmentEnd <= start)
                segmentEnd = Math.Min(
                    start + MaxGroundingSegmentLength,
                    endExclusive);

            var text = sourceText.Substring(
                start,
                segmentEnd - start);

            segments.Add(
                new GroundingSegment(
                    $"S{segments.Count + 1:D3}",
                    text,
                    start,
                    text.Length));

            start = segmentEnd;
            while (start < endExclusive &&
                   char.IsWhiteSpace(sourceText[start]))
            {
                start++;
            }
        }
    }

    private static string BuildGroundedInput(
        IReadOnlyList<GroundingSegment> groundingSegments) =>
        string.Join(
            '\n',
            groundingSegments.Select(
                segment => $"[{segment.Id}] {segment.Text}"));

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
            "Classify literature when it materially informs a candidate " +
            "requirement, whether it supports, establishes, corroborates, " +
            "or clarifies that requirement.");
        builder.AppendLine(
            "Use Clarifies when the article materially qualifies, limits, " +
            "contradicts, or provides balancing context for a candidate " +
            "requirement without itself supporting that requirement.");
        builder.AppendLine(
            "Do not treat balancing or contrary evidence as irrelevant solely " +
            "because it does not support the requirement.");
        builder.AppendLine(
            "Return no classification only when the article is not materially " +
            "relevant to any candidate requirement.");
        builder.AppendLine(
            "Return at most one classification for each unique combination " +
            "of candidate requirement and guidance role.");
        builder.AppendLine(
            "Every classification must identify at least one exact supporting " +
            "source segment by its bracketed segment ID. " +
            "Use only segment IDs present in the supplied article text. " +
            "Do not quote or paraphrase excerpts and do not return offsets " +
            "or lengths; EMF resolves segment IDs to exact source provenance.");
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
            "sourceSegmentIds": ["S001"]
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

        public required IReadOnlyList<string>
            SourceSegmentIds { get; init; }
    }

    private sealed record GroundingSegment(
        string Id,
        string Text,
        int StartOffset,
        int Length);
}
