using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceRecognitionTermRepository
{
    Task AddEvidenceRecognitionTermAsync(
        EvidenceRecognitionTerm term,
        CancellationToken cancellationToken = default);

    Task<EvidenceRecognitionTerm?>
        GetEvidenceRecognitionTermAsync(
            EvidenceRecognitionTermId termId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceRecognitionTerm>>
        GetEvidenceRecognitionTermsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default);
}
