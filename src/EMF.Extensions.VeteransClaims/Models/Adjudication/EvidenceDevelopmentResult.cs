using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentResult
{
    public required EvidenceGapId EvidenceGapId { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required IReadOnlyList<EvidenceRequirementGuidance>
        EvidenceGuidance { get; init; }

    public IReadOnlyList<EvidenceRecognitionMatch>
        RecognitionMatches { get; init; }
            = Array.Empty<EvidenceRecognitionMatch>();

    public IReadOnlyList<EvidenceRecognitionMatchArtifact>
        RecognitionMatchArtifacts { get; init; }
            = Array.Empty<EvidenceRecognitionMatchArtifact>();


    public int? MatchingGuidanceItemCount { get; init; }

    public int? MissingGuidanceItemCount { get; init; }

    public string? ResultingGapStatus { get; init; }
}
