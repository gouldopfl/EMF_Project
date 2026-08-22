using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class EmptyEvidenceRecognitionCoordinator :
    IEvidenceRecognitionCoordinator
{
    public Task<IReadOnlyList<EvidenceRecognitionMatch>>
        RecognizeAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EvidenceRecognitionMatch>>(
            Array.Empty<EvidenceRecognitionMatch>());
}
