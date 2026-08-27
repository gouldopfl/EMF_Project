using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ServiceConnectionTheoryOutcomeAssessmentService
{
    public ServiceConnectionTheoryOutcomeAssessment Assess(
        ServiceConnectionTheory theory,
        IReadOnlyList<ServiceConnectionBasisOutcomeAssessment>
            basisOutcomes)
    {
        ArgumentNullException.ThrowIfNull(theory);
        ArgumentNullException.ThrowIfNull(basisOutcomes);

        return new ServiceConnectionTheoryOutcomeAssessment
        {
            Theory = theory,
            BasisOutcomes = basisOutcomes,
            Outcome = DeriveOutcome(basisOutcomes)
        };
    }

    private static string DeriveOutcome(
        IReadOnlyCollection<ServiceConnectionBasisOutcomeAssessment>
            outcomes)
    {
        if (outcomes.Count == 0)
            return FindingOutcomes.Unresolved;

        if (outcomes.Any(
            x => x.Outcome == FindingOutcomes.Favorable))
        {
            return FindingOutcomes.Favorable;
        }

        if (outcomes.Any(
            x => x.Outcome == FindingOutcomes.PartiallyFavorable))
        {
            return FindingOutcomes.PartiallyFavorable;
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

        if (outcomes.All(
            x => x.Outcome == FindingOutcomes.Unfavorable))
        {
            return FindingOutcomes.Unfavorable;
        }

        throw new InvalidOperationException(
            "Unable to derive service-connection theory outcome.");
    }
}
