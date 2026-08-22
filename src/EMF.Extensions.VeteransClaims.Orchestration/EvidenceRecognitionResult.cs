using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class EvidenceRecognitionResult
{
    public required IReadOnlyList<EvidenceRecognitionMatch>
        Matches { get; init; }

    public required IReadOnlyList<EvidenceRecognitionMatchArtifact>
        MatchArtifacts { get; init; }
}
