using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueMeritsOutcomeAssessmentService
{
    public ClaimIssueMeritsOutcomeAssessment Assess(
        ClaimIssueId claimIssueId,
        IReadOnlyList<ServiceConnectionTheoryOutcomeAssessment>
            theoryOutcomes)
    {
        ArgumentNullException.ThrowIfNull(theoryOutcomes);

        return new ClaimIssueMeritsOutcomeAssessment
        {
            ClaimIssueId = claimIssueId,
            TheoryOutcomes = theoryOutcomes,
            Outcome = DeriveOutcome(theoryOutcomes)
        };
    }

    private static string DeriveOutcome(
        IReadOnlyCollection<ServiceConnectionTheoryOutcomeAssessment>
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
            "Unable to derive claim-issue merits outcome.");
    }
}
