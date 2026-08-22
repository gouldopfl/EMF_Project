using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceRecognitionTerm
{
    public required EvidenceRecognitionTermId Id { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required string Term { get; init; }

    public required string TermType { get; init; }

    public required string RecognitionRole { get; init; }

    public required string AuthoritySource { get; init; }
}
