using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentPlanStatus
{
    public required EvidenceDevelopmentPlanId PlanId { get; init; }

    public required string Status { get; init; }

    public bool IsComplete =>
        Status == EvidenceDevelopmentPlanStatuses.Complete;

    public bool RequiresDevelopment =>
        Status == EvidenceDevelopmentPlanStatuses.RequiresDevelopment;
}
