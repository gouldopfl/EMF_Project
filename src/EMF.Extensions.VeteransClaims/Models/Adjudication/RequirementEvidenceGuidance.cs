using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementEvidenceGuidance
{
    public required Requirement Requirement { get; init; }

    public required IReadOnlyList<EvidenceRequirementGuidance>
        EvidenceGuidance { get; init; }
}
