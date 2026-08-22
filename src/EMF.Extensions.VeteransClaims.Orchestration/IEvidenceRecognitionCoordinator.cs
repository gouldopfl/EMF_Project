using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal interface IEvidenceRecognitionCoordinator
{
    Task<IReadOnlyList<EvidenceRecognitionMatch>>
        RecognizeAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default);
}
