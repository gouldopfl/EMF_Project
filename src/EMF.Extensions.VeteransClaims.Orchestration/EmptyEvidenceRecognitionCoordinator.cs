namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class EmptyEvidenceRecognitionCoordinator :
    IEvidenceRecognitionCoordinator
{
    public Task<EvidenceRecognitionResult>
        RecognizeAsync(
            Models.Identities.EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new EvidenceRecognitionResult
            {
                Matches = [],
                MatchArtifacts = []
            });
}
