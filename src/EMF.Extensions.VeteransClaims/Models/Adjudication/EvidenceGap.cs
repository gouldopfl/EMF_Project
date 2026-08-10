using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceGap
{
    public required EvidenceGapId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required string Description { get; init; }
}
