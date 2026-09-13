using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceRecognitionTermReplaySnapshotContext
{
    public required ServiceConnectionBasisId BasisId
    { get; init; }

    public required RequirementId RequirementId
    { get; init; }

    public required IReadOnlyList<EvidenceRecognitionTermProposal> Proposals
    { get; init; }
}

public sealed class EvidenceRecognitionTermReplaySnapshotParser
{
    private const int MaximumContexts = 64;
    private const int MaximumProposalsPerContext = 64;

    public IReadOnlyList<EvidenceRecognitionTermReplaySnapshotContext>
        Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var snapshot =
            JsonSerializer.Deserialize<SnapshotDto>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidDataException(
                "Recognition replay snapshot contained no data.");

        if (snapshot.Contexts is null || snapshot.Contexts.Count == 0)
        {
            throw new InvalidDataException(
                "Recognition replay snapshot contains no contexts.");
        }

        if (snapshot.Contexts.Count > MaximumContexts)
        {
            throw new InvalidDataException(
                $"Recognition replay snapshot exceeds the maximum of " +
                $"{MaximumContexts} contexts.");
        }

        var contexts =
            new List<EvidenceRecognitionTermReplaySnapshotContext>(
                snapshot.Contexts.Count);

        var contextIdentities =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in snapshot.Contexts)
        {
            var basisId = Required(item.BasisId, "Basis ID");
            var requirementId = Required(item.RequirementId, "Requirement ID");

            var contextIdentity = basisId + "\u001f" + requirementId;

            if (!contextIdentities.Add(contextIdentity))
            {
                throw new InvalidDataException(
                    "Recognition replay snapshot contains duplicate " +
                    "basis/requirement contexts.");
            }

            if (item.Proposals is null)
            {
                throw new InvalidDataException(
                    "Recognition replay snapshot context has no proposal collection.");
            }

            if (item.Proposals.Count > MaximumProposalsPerContext)
            {
                throw new InvalidDataException(
                    $"Recognition replay context exceeds the maximum of " +
                    $"{MaximumProposalsPerContext} proposals.");
            }

            var proposals =
                new List<EvidenceRecognitionTermProposal>(
                    item.Proposals.Count);

            var proposalIdentities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var proposal in item.Proposals)
            {
                var proposalRequirementId =
                    Required(
                        proposal.RequirementId,
                        "Proposal requirement ID");

                if (!string.Equals(
                        proposalRequirementId,
                        requirementId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Recognition replay proposal requirement does not " +
                        "match its context requirement.");
                }

                var term = Required(proposal.Term, "Recognition term");
                var termType = Required(proposal.TermType, "Recognition term type");
                var recognitionRole =
                    Required(
                        proposal.RecognitionRole,
                        "Recognition role");
                var authoritySource =
                    Required(
                        proposal.AuthoritySource,
                        "Authority source");
                var rationale =
                    Required(
                        proposal.Rationale,
                        "Recognition rationale");

                ValidateTermType(termType);
                ValidateRecognitionRole(recognitionRole);
                ValidateEvidenceClassification(
                    proposal.EvidenceClassification);

                var proposalIdentity =
                    string.Join(
                        "\u001f",
                        term,
                        termType,
                        recognitionRole,
                        proposal.EvidenceClassification ?? string.Empty);

                if (!proposalIdentities.Add(proposalIdentity))
                {
                    throw new InvalidDataException(
                        "Recognition replay context contains duplicate proposals.");
                }

                proposals.Add(
                    new EvidenceRecognitionTermProposal
                    {
                        RequirementId =
                            new RequirementId(requirementId),
                        Term = term,
                        TermType = termType,
                        RecognitionRole = recognitionRole,
                        EvidenceClassification =
                            proposal.EvidenceClassification,
                        AuthoritySource = authoritySource,
                        Rationale = rationale
                    });
            }

            contexts.Add(
                new EvidenceRecognitionTermReplaySnapshotContext
                {
                    BasisId =
                        new ServiceConnectionBasisId(basisId),
                    RequirementId =
                        new RequirementId(requirementId),
                    Proposals = proposals
                });
        }

        return contexts;
    }

    private static string Required(
        string? value,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{label} must not be blank.");

        return value.Trim();
    }

    private static void ValidateTermType(string value)
    {
        if (value is not (
            EvidenceRecognitionTermTypes.Keyword or
            EvidenceRecognitionTermTypes.Phrase or
            EvidenceRecognitionTermTypes.Acronym or
            EvidenceRecognitionTermTypes.Synonym))
        {
            throw new InvalidDataException(
                $"Unsupported recognition term type '{value}'.");
        }
    }

    private static void ValidateRecognitionRole(string value)
    {
        if (value is not (
            EvidenceRecognitionRoles.Diagnosis or
            EvidenceRecognitionRoles.SeverityCriterion or
            EvidenceRecognitionRoles.FunctionalImpact or
            EvidenceRecognitionRoles.ServiceConnection or
            EvidenceRecognitionRoles.MedicalNexus or
            EvidenceRecognitionRoles.Aggravation or
            EvidenceRecognitionRoles.Presumptive or
            EvidenceRecognitionRoles.EvidenceType))
        {
            throw new InvalidDataException(
                $"Unsupported recognition role '{value}'.");
        }
    }

    private static void ValidateEvidenceClassification(string? value)
    {
        if (value is null)
            return;

        if (value is not (
            EvidenceClassifications.MedicalEvidence or
            EvidenceClassifications.ServiceTreatmentRecord or
            EvidenceClassifications.ServiceRecord or
            EvidenceClassifications.LayEvidence or
            EvidenceClassifications.Examination or
            EvidenceClassifications.MedicalOpinion or
            EvidenceClassifications.AdjudicativeRecord))
        {
            throw new InvalidDataException(
                $"Unsupported evidence classification '{value}'.");
        }
    }

    private sealed class SnapshotDto
    {
        public IReadOnlyList<ContextDto>? Contexts { get; init; }
    }

    private sealed class ContextDto
    {
        public string? BasisId { get; init; }

        public string? RequirementId { get; init; }

        public IReadOnlyList<ProposalDto>? Proposals { get; init; }
    }

    private sealed class ProposalDto
    {
        public string? RequirementId { get; init; }

        public string? Term { get; init; }

        public string? TermType { get; init; }

        public string? RecognitionRole { get; init; }

        public string? EvidenceClassification { get; init; }

        public string? AuthoritySource { get; init; }

        public string? Rationale { get; init; }
    }
}
