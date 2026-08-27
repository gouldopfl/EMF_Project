using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionTheoryOutcomeAssessment
{
    public required ServiceConnectionTheory Theory { get; init; }

    public required IReadOnlyList<ServiceConnectionBasisOutcomeAssessment>
        BasisOutcomes { get; init; }

    public required string Outcome { get; init; }
}
