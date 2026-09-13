using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceRecognitionTermService
{
    private readonly IRegulatoryRepository _regulatory;
    private readonly IEvidenceRecognitionTermRepository _terms;
    private readonly IIdGenerator _idGenerator;

    public EvidenceRecognitionTermService(
        IRegulatoryRepository regulatory,
        IEvidenceRecognitionTermRepository terms,
        IIdGenerator? idGenerator = null)
    {
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(terms);

        _regulatory = regulatory;
        _terms = terms;
        _idGenerator = idGenerator ?? new GuidIdGenerator();
    }

    public async Task<EvidenceRecognitionTerm> AddAsync(
        RequirementId requirementId,
        string term,
        string termType,
        string recognitionRole,
        string? evidenceClassification,
        string authoritySource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        ArgumentException.ThrowIfNullOrWhiteSpace(termType);
        ArgumentException.ThrowIfNullOrWhiteSpace(recognitionRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritySource);

        if (!IsSupportedTermType(termType))
        {
            throw new ArgumentException(
                $"Unsupported evidence recognition term type '{termType}'.",
                nameof(termType));
        }

        if (!IsSupportedRecognitionRole(recognitionRole))
        {
            throw new ArgumentException(
                $"Unsupported evidence recognition role '{recognitionRole}'.",
                nameof(recognitionRole));
        }

        if (evidenceClassification is not null &&
            !IsSupportedEvidenceClassification(evidenceClassification))
        {
            throw new ArgumentException(
                $"Unsupported evidence classification '{evidenceClassification}'.",
                nameof(evidenceClassification));
        }

        var requirement =
            await _regulatory.GetRequirementAsync(
                requirementId,
                cancellationToken);

        if (requirement is null)
        {
            throw new InvalidOperationException(
                $"Requirement not found: {requirementId.Value}");
        }

        if (requirement.Id != requirementId)
        {
            throw new InvalidOperationException(
                "Evidence recognition requirement identity mismatch.");
        }

        var normalizedTerm = term.Trim();
        var normalizedAuthority = authoritySource.Trim();

        var existing =
            await _terms.GetEvidenceRecognitionTermsAsync(
                requirementId,
                cancellationToken);

        if (existing.Any(x => x.RequirementId != requirementId))
        {
            throw new InvalidOperationException(
                $"Requirement '{requirementId.Value}' recognition lookup " +
                "returned a term for a different requirement.");
        }

        var matching =
            existing.FirstOrDefault(x =>
                string.Equals(
                    x.Term,
                    normalizedTerm,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.TermType,
                    termType,
                    StringComparison.Ordinal) &&
                string.Equals(
                    x.RecognitionRole,
                    recognitionRole,
                    StringComparison.Ordinal) &&
                string.Equals(
                    x.EvidenceClassification,
                    evidenceClassification,
                    StringComparison.Ordinal) &&
                string.Equals(
                    x.AuthoritySource,
                    normalizedAuthority,
                    StringComparison.OrdinalIgnoreCase));

        if (matching is not null)
            return matching;

        var result =
            new EvidenceRecognitionTerm
            {
                Id =
                    new EvidenceRecognitionTermId(
                        _idGenerator.Generate()),
                RequirementId = requirementId,
                Term = normalizedTerm,
                TermType = termType,
                RecognitionRole = recognitionRole,
                EvidenceClassification = evidenceClassification,
                AuthoritySource = normalizedAuthority
            };

        await _terms.AddEvidenceRecognitionTermAsync(
            result,
            cancellationToken);

        return result;
    }

    private static bool IsSupportedTermType(string value) =>
        value is
            EvidenceRecognitionTermTypes.Keyword or
            EvidenceRecognitionTermTypes.Phrase or
            EvidenceRecognitionTermTypes.Acronym or
            EvidenceRecognitionTermTypes.Synonym;

    private static bool IsSupportedRecognitionRole(string value) =>
        value is
            EvidenceRecognitionRoles.Diagnosis or
            EvidenceRecognitionRoles.SeverityCriterion or
            EvidenceRecognitionRoles.FunctionalImpact or
            EvidenceRecognitionRoles.ServiceConnection or
            EvidenceRecognitionRoles.MedicalNexus or
            EvidenceRecognitionRoles.Aggravation or
            EvidenceRecognitionRoles.Presumptive or
            EvidenceRecognitionRoles.EvidenceType;

    private static bool IsSupportedEvidenceClassification(string value) =>
        value is
            EvidenceClassifications.MedicalEvidence or
            EvidenceClassifications.ServiceTreatmentRecord or
            EvidenceClassifications.ServiceRecord or
            EvidenceClassifications.LayEvidence or
            EvidenceClassifications.Examination or
            EvidenceClassifications.MedicalOpinion or
            EvidenceClassifications.AdjudicativeRecord;
}
