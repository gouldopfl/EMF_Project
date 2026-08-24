namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementEvidenceResponsivenessItem
{
    public required EvidenceRequirementGuidance Guidance { get; init; }

    public required bool HasMatchingEvidence { get; init; }
}
