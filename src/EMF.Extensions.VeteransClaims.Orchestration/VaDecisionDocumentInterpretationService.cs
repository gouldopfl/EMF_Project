using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class VaDecisionDocumentInterpretationService
{
    private readonly IIntelligenceCapabilityExecutor<
        TextStructuredExtractionRequest,
        string> _executor;

    private readonly VaDecisionDocumentInterpretationValidator
        _validator = new();

    public VaDecisionDocumentInterpretationService(
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }
    public async Task<VaDecisionDocumentInterpretationResult>
        InterpretAsync(
            ArtifactId artifactId,
            string text,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(context);

        var result =
            await _executor.ExecuteAsync(
                IntelligenceCapabilityIds.TextStructuredExtraction,
                new TextStructuredExtractionRequest(
                    text,
                    BuildInstruction(),
                    BuildJsonShape()),
                context,
                cancellationToken);

        if (!result.Success)
        {
            return new VaDecisionDocumentInterpretationResult
            {
                IntelligenceResult = result
            };
        }

        var extracted =
            JsonSerializer.Deserialize<ExtractedDecision>(
                result.Output!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidOperationException(
                "Structured decision extraction returned no decision.");

        var interpretation =
            Map(artifactId, extracted);

        _validator.ValidateAgainstSource(
            interpretation,
            text);

        return new VaDecisionDocumentInterpretationResult
        {
            IntelligenceResult = result,
            Interpretation = interpretation
        };
    }

    private static VaDecisionDocumentInterpretation Map(
        ArtifactId artifactId,
        ExtractedDecision extracted) =>
        new()
        {
            ArtifactId = artifactId,
            DecisionDate = extracted.DecisionDate,
            IssueDecisions =
                extracted.IssueDecisions.Select(
                    issue =>
                        new VaIssueDecisionInterpretation
                        {
                            IssueDescription = issue.IssueDescription,
                            Outcome = issue.Outcome,
                            Rationale = issue.Rationale,
                            FavorableFindings = issue.FavorableFindings,
                            AdverseFindings = issue.AdverseFindings,
                            CitedRegulations = issue.CitedRegulations,
                            ReferencedEvidence = issue.ReferencedEvidence,
                            SourceExcerpts =
                                issue.SourceExcerpts.Select(
                                    excerpt =>
                                        new DecisionDocumentSourceExcerpt
                                        {
                                            ArtifactId = artifactId,
                                            Text = excerpt.Text,
                                            StartOffset = excerpt.StartOffset,
                                            Length = excerpt.Length
                                        }).ToArray()
                        }).ToArray()
        };

    private static string BuildInstruction() =>
        """
        Extract the VA decision date and every decided issue.
        Use only information present in the supplied decision text.
        Preserve the issue description, outcome, rationale, favorable
        findings, adverse findings, cited regulations, referenced
        evidence, and supporting source excerpts.
        Outcomes must be Granted, Denied, Deferred, or PartiallyGranted.
        Do not invent findings, evidence, regulations, or rationale.
        """;

    private static string BuildJsonShape() =>
        """
        {
          "decisionDate": "date-time or null",
          "issueDecisions": [{
            "issueDescription": "string",
            "outcome": "Granted|Denied|Deferred|PartiallyGranted",
            "rationale": "string",
            "favorableFindings": ["string"],
            "adverseFindings": ["string"],
            "citedRegulations": ["string"],
            "referencedEvidence": ["string"],
            "sourceExcerpts": [{
              "text": "string",
              "startOffset": 0,
              "length": 0
            }]
          }]
        }
        """;

    private sealed class ExtractedDecision
    {
        public DateTimeOffset? DecisionDate { get; init; }

        public required IReadOnlyList<ExtractedIssue>
            IssueDecisions { get; init; }
    }

    private sealed class ExtractedIssue
    {
        public required string IssueDescription { get; init; }
        public required string Outcome { get; init; }
        public required string Rationale { get; init; }

        public required IReadOnlyList<string>
            FavorableFindings { get; init; }

        public required IReadOnlyList<string>
            AdverseFindings { get; init; }

        public required IReadOnlyList<string>
            CitedRegulations { get; init; }

        public required IReadOnlyList<string>
            ReferencedEvidence { get; init; }

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
