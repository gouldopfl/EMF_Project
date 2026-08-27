using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementFindingAssessment
{
    public required RequirementId RequirementId { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }

    public bool HasFindings =>
        Findings.Count > 0;
}
