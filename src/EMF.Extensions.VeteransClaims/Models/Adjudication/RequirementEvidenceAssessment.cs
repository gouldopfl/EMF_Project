using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementEvidenceAssessment
{
    public required RequirementId RequirementId { get; init; }

    public required IReadOnlyList<EvidenceClassification>
        Evidence { get; init; }

    public bool HasEvidence => Evidence.Count > 0;
}
