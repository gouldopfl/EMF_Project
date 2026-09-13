using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceRecognitionTermProposal
{
    public required RequirementId RequirementId
    { get; init; }

    public required string Term
    { get; init; }

    public required string TermType
    { get; init; }

    public required string RecognitionRole
    { get; init; }

    public string? EvidenceClassification
    { get; init; }

    public required string AuthoritySource
    { get; init; }

    public required string Rationale
    { get; init; }
}

public sealed class EvidenceRecognitionTermProposalResult
{
    public required IntelligenceCapabilityResult<string>
        IntelligenceResult { get; init; }

    public IReadOnlyList<EvidenceRecognitionTermProposal> Proposals
    { get; init; } = [];
}
