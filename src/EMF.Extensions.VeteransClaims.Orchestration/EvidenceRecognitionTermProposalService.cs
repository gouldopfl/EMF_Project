using System.Text;
using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class EvidenceRecognitionTermProposalService
{
    public const int MaximumProposals = 24;
    public const int MaximumTermCharacters = 200;
    public const int MaximumRationaleCharacters = 1_000;

    private readonly IIntelligenceCapabilityExecutor<
        TextStructuredExtractionRequest,
        string> _executor;

    public EvidenceRecognitionTermProposalService(
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public async Task<EvidenceRecognitionTermProposalResult>
        ProposeAsync(
            Requirement requirement,
            string claimedCondition,
            IReadOnlyList<string> serviceConnectedConditions,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedCondition);
        ArgumentNullException.ThrowIfNull(serviceConnectedConditions);
        ArgumentNullException.ThrowIfNull(context);

        if (serviceConnectedConditions.Any(
            string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Service-connected condition names must not be blank.",
                nameof(serviceConnectedConditions));
        }

        var source =
            BuildSource(
                requirement,
                claimedCondition.Trim(),
                serviceConnectedConditions);

        var result =
            await _executor.ExecuteAsync(
                IntelligenceCapabilityIds.TextStructuredExtraction,
                new TextStructuredExtractionRequest(
                    source,
                    BuildInstruction(requirement),
                    BuildJsonShape()),
                context,
                cancellationToken);

        if (!result.Success)
        {
            return new EvidenceRecognitionTermProposalResult
            {
                IntelligenceResult = result
            };
        }

        var extracted =
            JsonSerializer.Deserialize<ExtractedProposalSet>(
                result.Output!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidOperationException(
                "Structured recognition-term extraction returned no proposal set.");

        var proposals =
            ValidateAndMap(
                requirement,
                extracted);

        return new EvidenceRecognitionTermProposalResult
        {
            IntelligenceResult = result,
            Proposals = proposals
        };
    }

    private static IReadOnlyList<EvidenceRecognitionTermProposal>
        ValidateAndMap(
            Requirement requirement,
            ExtractedProposalSet extracted)
    {
        ArgumentNullException.ThrowIfNull(extracted.Terms);

        if (extracted.Terms.Count > MaximumProposals)
        {
            throw new InvalidOperationException(
                $"Recognition-term proposal exceeded the maximum of " +
                $"{MaximumProposals} terms.");
        }

        var proposals =
            new List<EvidenceRecognitionTermProposal>(
                extracted.Terms.Count);

        var identities =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in extracted.Terms)
        {
            if (!string.Equals(
                    item.RequirementId,
                    requirement.Id.Value,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Recognition-term proposal returned a term for an " +
                    "unexpected requirement.");
            }

            var term = RequiredTrimmed(
                item.Term,
                "Recognition term");

            if (term.Length > MaximumTermCharacters)
            {
                throw new InvalidOperationException(
                    "Recognition term exceeds the maximum allowed length.");
            }

            var rationale = RequiredTrimmed(
                item.Rationale,
                "Recognition-term rationale");

            if (rationale.Length > MaximumRationaleCharacters)
            {
                throw new InvalidOperationException(
                    "Recognition-term rationale exceeds the maximum allowed length.");
            }

            ValidateTermType(item.TermType);
            ValidateRecognitionRole(item.RecognitionRole);
            ValidateEvidenceClassification(
                item.EvidenceClassification);

            var identity = string.Join(
                "\u001f",
                term,
                item.TermType,
                item.RecognitionRole,
                item.EvidenceClassification ?? string.Empty);

            if (!identities.Add(identity))
            {
                throw new InvalidOperationException(
                    "Structured recognition-term extraction returned a duplicate proposal.");
            }

            proposals.Add(
                new EvidenceRecognitionTermProposal
                {
                    RequirementId = requirement.Id,
                    Term = term,
                    TermType = item.TermType,
                    RecognitionRole = item.RecognitionRole,
                    EvidenceClassification =
                        item.EvidenceClassification,
                    AuthoritySource =
                        requirement.RegulatoryProvisionId.Value,
                    Rationale = rationale
                });
        }

        return proposals;
    }

    private static string RequiredTrimmed(
        string? value,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"{label} must not be blank.");

        return value.Trim();
    }

    private static void ValidateTermType(string termType)
    {
        if (termType is not (
            EvidenceRecognitionTermTypes.Keyword or
            EvidenceRecognitionTermTypes.Phrase or
            EvidenceRecognitionTermTypes.Acronym or
            EvidenceRecognitionTermTypes.Synonym))
        {
            throw new InvalidOperationException(
                $"Unsupported recognition term type '{termType}'.");
        }
    }

    private static void ValidateRecognitionRole(
        string recognitionRole)
    {
        if (recognitionRole is not (
            EvidenceRecognitionRoles.Diagnosis or
            EvidenceRecognitionRoles.SeverityCriterion or
            EvidenceRecognitionRoles.FunctionalImpact or
            EvidenceRecognitionRoles.ServiceConnection or
            EvidenceRecognitionRoles.MedicalNexus or
            EvidenceRecognitionRoles.Aggravation or
            EvidenceRecognitionRoles.Presumptive or
            EvidenceRecognitionRoles.EvidenceType))
        {
            throw new InvalidOperationException(
                $"Unsupported recognition role '{recognitionRole}'.");
        }
    }

    private static void ValidateEvidenceClassification(
        string? evidenceClassification)
    {
        if (evidenceClassification is null)
            return;

        if (evidenceClassification is not (
            EvidenceClassifications.MedicalEvidence or
            EvidenceClassifications.ServiceTreatmentRecord or
            EvidenceClassifications.ServiceRecord or
            EvidenceClassifications.LayEvidence or
            EvidenceClassifications.Examination or
            EvidenceClassifications.MedicalOpinion or
            EvidenceClassifications.AdjudicativeRecord))
        {
            throw new InvalidOperationException(
                $"Unsupported evidence classification " +
                $"'{evidenceClassification}'.");
        }
    }

    private static string BuildSource(
        Requirement requirement,
        string claimedCondition,
        IReadOnlyList<string> serviceConnectedConditions)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            $"Requirement ID: {requirement.Id.Value}");
        builder.AppendLine(
            $"Requirement: {requirement.Description}");
        builder.AppendLine(
            $"Claimed condition: {claimedCondition}");
        builder.AppendLine(
            "Service-connected basis conditions:");

        foreach (var condition in
            serviceConnectedConditions
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {condition}");
        }

        return builder.ToString();
    }

    private static string BuildInstruction(
        Requirement requirement) =>
        $"""
        Propose recognition terms that deterministic software can use to find
        potentially relevant evidence for the supplied VA claim requirement.
        The terms are search vocabulary, not medical findings and not an
        adjudication. Use only the supplied requirement and condition context.
        Do not invent diagnoses, symptoms, medications, treatment, events,
        medical relationships, or patient facts. Prefer specific clinical
        phrases, recognized acronyms, and meaningful synonyms over generic
        words. Return at most {MaximumProposals} terms. Every returned term
        must use requirementId '{requirement.Id.Value}'. EvidenceClassification
        may be null. Allowed termType values: Keyword, Phrase, Acronym, Synonym.
        Allowed recognitionRole values: Diagnosis, SeverityCriterion,
        FunctionalImpact, ServiceConnection, MedicalNexus, Aggravation,
        Presumptive, EvidenceType. Allowed evidenceClassification values:
        MedicalEvidence, ServiceTreatmentRecord, ServiceRecord, LayEvidence,
        Examination, MedicalOpinion, AdjudicativeRecord, or null. Provide a
        short rationale explaining why each term may identify relevant evidence.
        """;

    private static string BuildJsonShape() =>
        """
        {
          "terms": [{
            "requirementId": "string",
            "term": "string",
            "termType": "Keyword|Phrase|Acronym|Synonym",
            "recognitionRole": "Diagnosis|SeverityCriterion|FunctionalImpact|ServiceConnection|MedicalNexus|Aggravation|Presumptive|EvidenceType",
            "evidenceClassification": "MedicalEvidence|ServiceTreatmentRecord|ServiceRecord|LayEvidence|Examination|MedicalOpinion|AdjudicativeRecord|null",
            "rationale": "string"
          }]
        }
        """;

    private sealed class ExtractedProposalSet
    {
        public required IReadOnlyList<ExtractedProposal>
            Terms { get; init; }
    }

    private sealed class ExtractedProposal
    {
        public required string RequirementId { get; init; }
        public required string Term { get; init; }
        public required string TermType { get; init; }
        public required string RecognitionRole { get; init; }
        public string? EvidenceClassification { get; init; }
        public required string Rationale { get; init; }
    }
}
