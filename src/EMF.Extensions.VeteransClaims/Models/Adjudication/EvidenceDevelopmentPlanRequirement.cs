using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentPlanRequirement
{
    public required EvidenceDevelopmentPlanId EvidenceDevelopmentPlanId { get; init; }

    public required RequirementId RequirementId { get; init; }
}
