using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassificationRequirement
{
    public required EvidenceClassificationId EvidenceClassificationId { get; init; }

    public required RequirementId RequirementId { get; init; }
}
