using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementFindingOutcomeAssessment
{
    public required RequirementId RequirementId { get; init; }

    public required string Outcome { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }
}
