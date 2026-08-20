using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentResult
{
    public required EvidenceGapId EvidenceGapId { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required IReadOnlyList<EvidenceRequirementGuidance>
        EvidenceGuidance { get; init; }
}
