using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceRecognitionMatch
{
    public required EvidenceRecognitionTermId TermId { get; init; }

    public required string Term { get; init; }

    public required string RecognitionRole { get; init; }

    public string? EvidenceClassification { get; init; }

    public required string AuthoritySource { get; init; }
}
