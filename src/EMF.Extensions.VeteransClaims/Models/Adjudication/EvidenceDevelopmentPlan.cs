using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentPlan
{
    public required EvidenceDevelopmentPlanId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Description { get; init; }
}
