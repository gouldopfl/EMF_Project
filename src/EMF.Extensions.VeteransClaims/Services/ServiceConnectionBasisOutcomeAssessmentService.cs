using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ServiceConnectionBasisOutcomeAssessmentService
{
    public ServiceConnectionBasisOutcomeAssessment Assess(
        ServiceConnectionBasis basis,
        IReadOnlyList<RequirementFindingOutcomeAssessment>
            requirementOutcomes)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(requirementOutcomes);

        return new ServiceConnectionBasisOutcomeAssessment
        {
            Basis = basis,
            RequirementOutcomes = requirementOutcomes,
            Outcome = DeriveOutcome(requirementOutcomes)
        };
    }

    private static string DeriveOutcome(
        IReadOnlyCollection<RequirementFindingOutcomeAssessment>
            outcomes)
    {
        if (outcomes.Count == 0)
            return FindingOutcomes.Unresolved;

        if (outcomes.Any(
            x => x.Outcome == FindingOutcomes.Unfavorable))
        {
            return FindingOutcomes.Unfavorable;
        }

        if (outcomes.Any(
            x => x.Outcome == FindingOutcomes.Disputed))
        {
            return FindingOutcomes.Disputed;
        }

        if (outcomes.Any(
            x => x.Outcome == FindingOutcomes.Unresolved))
        {
            return FindingOutcomes.Unresolved;
        }

        if (outcomes.Any(
            x => x.Outcome == FindingOutcomes.PartiallyFavorable))
        {
            return FindingOutcomes.PartiallyFavorable;
        }

        if (outcomes.All(
            x => x.Outcome == FindingOutcomes.Favorable))
        {
            return FindingOutcomes.Favorable;
        }

        throw new InvalidOperationException(
            "Unable to derive service-connection basis outcome.");
    }
}
