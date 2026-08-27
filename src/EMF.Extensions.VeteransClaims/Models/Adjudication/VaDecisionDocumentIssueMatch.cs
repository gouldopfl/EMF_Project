using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecisionDocumentIssueMatch
{
    public required VaIssueDecisionInterpretation
        Interpretation { get; init; }

    public required string Status { get; init; }

    public ClaimIssueId? ClaimIssueId { get; init; }

    public required IReadOnlyList<ClaimIssueId>
        CandidateClaimIssueIds { get; init; }
}
