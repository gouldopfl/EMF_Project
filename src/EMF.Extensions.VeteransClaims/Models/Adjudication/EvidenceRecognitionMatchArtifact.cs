using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceRecognitionMatchArtifact
{
    public required EvidenceRecognitionTermId
        RecognitionTermId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
