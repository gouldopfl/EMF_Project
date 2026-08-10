using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentPlanEvidenceGap
{
    public required EvidenceDevelopmentPlanId EvidenceDevelopmentPlanId { get; init; }

    public required EvidenceGapId EvidenceGapId { get; init; }
}
