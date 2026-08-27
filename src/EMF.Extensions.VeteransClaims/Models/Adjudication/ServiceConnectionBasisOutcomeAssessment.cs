using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisOutcomeAssessment
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required IReadOnlyList<RequirementFindingOutcomeAssessment>
        RequirementOutcomes { get; init; }

    public required string Outcome { get; init; }
}
