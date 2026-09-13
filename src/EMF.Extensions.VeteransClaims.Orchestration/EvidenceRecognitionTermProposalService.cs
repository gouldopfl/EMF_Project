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
    public const int MaximumProposals = 12;
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
        Propose a small, precision-oriented set of recognition terms that
        deterministic software can use to nominate potentially relevant evidence
        for the supplied VA claim requirement. The matcher performs only a
        case-insensitive literal substring search against one source record at a
        time. It does not use stemming, regex, semantic similarity, or inference.
        Therefore propose text that is reasonably likely to occur verbatim in
        real clinical or evidentiary records.

        The terms are retrieval vocabulary, not medical findings and not an
        adjudication. Use only the supplied requirement and condition context.
        Do not invent diagnoses, symptoms, medications, treatment, events,
        medical mechanisms, patient facts, or relationships between conditions.
        Do not combine supplied condition names into hypothetical nexus sentences
        such as "condition A secondary to condition B" merely because that
        sentence would satisfy the requirement.

        Prefer short lexical anchors: supplied condition names, commonly used
        abbreviations or genuine synonyms of those names, and concise
        requirement-specific relationship, severity, or functional language that
        is likely to appear literally in records. Most terms should be one to five
        words. Avoid long synthetic phrases, legal boilerplate, generic document
        labels, and terms that merely restate the full requirement.

        Keep each proposal within the scope of this requirement. Do not propose
        Aggravation terms unless the supplied requirement addresses aggravation.
        Do not propose causation or secondary-nexus terms unless the supplied
        requirement addresses causation or nexus. EvidenceClassification should
        be null unless the proposed literal text is strongly characteristic of a
        specific evidence classification.

        Quality is more important than quantity. Do not fill a quota. Return only
        distinct high-value terms, normally 6 to 10 and never more than
        {MaximumProposals}. Every returned term must use requirementId
        '{requirement.Id.Value}'. Allowed termType values: Keyword, Phrase,
        Acronym, Synonym. Allowed recognitionRole values: Diagnosis,
        SeverityCriterion, FunctionalImpact, ServiceConnection, MedicalNexus,
        Aggravation, Presumptive, EvidenceType. Allowed evidenceClassification
        values: MedicalEvidence, ServiceTreatmentRecord, ServiceRecord,
        LayEvidence, Examination, MedicalOpinion, AdjudicativeRecord, or null.
        Provide a short rationale explaining why the literal term may identify
        relevant evidence.
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
