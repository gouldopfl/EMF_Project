using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class CreateEvidenceDevelopmentPlanRequest
{
    public required EvidenceDevelopmentPlanId PlanId { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<EvidenceGapId>
        EvidenceGapIds { get; init; }
}
