using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal interface IEvidenceRecognitionCoordinator
{
    Task<EvidenceRecognitionResult>
        RecognizeAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default);
}
