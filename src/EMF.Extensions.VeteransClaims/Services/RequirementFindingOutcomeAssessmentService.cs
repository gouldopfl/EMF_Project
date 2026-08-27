using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class RequirementFindingOutcomeAssessmentService
{
    public RequirementFindingOutcomeAssessment Assess(
        RequirementFindingAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var outcomes =
            assessment.Findings
                .Select(x => x.Outcome)
                .Distinct()
                .ToArray();

        foreach (var outcome in outcomes)
        {
            if (outcome != FindingOutcomes.Favorable &&
                outcome != FindingOutcomes.Unfavorable &&
                outcome != FindingOutcomes.PartiallyFavorable &&
                outcome != FindingOutcomes.Unresolved &&
                outcome != FindingOutcomes.Disputed)
            {
                throw new InvalidOperationException(
                    $"Unknown finding outcome: {outcome}");
            }
        }

        var derived =
            DeriveOutcome(outcomes);

        return new RequirementFindingOutcomeAssessment
        {
            RequirementId = assessment.RequirementId,
            Outcome = derived,
            Findings = assessment.Findings
        };
    }

    private static string DeriveOutcome(
        IReadOnlyCollection<string> outcomes)
    {
        if (outcomes.Count == 0)
            return FindingOutcomes.Unresolved;

        if (outcomes.Contains(FindingOutcomes.Disputed))
            return FindingOutcomes.Disputed;

        if (outcomes.Contains(FindingOutcomes.Favorable) &&
            outcomes.Contains(FindingOutcomes.Unfavorable))
        {
            return FindingOutcomes.Disputed;
        }

        if (outcomes.Contains(FindingOutcomes.Unfavorable) &&
            outcomes.Contains(FindingOutcomes.PartiallyFavorable))
        {
            return FindingOutcomes.Disputed;
        }

        if (outcomes.Contains(FindingOutcomes.Unresolved))
            return FindingOutcomes.Unresolved;

        if (outcomes.Contains(FindingOutcomes.PartiallyFavorable))
            return FindingOutcomes.PartiallyFavorable;

        if (outcomes.Contains(FindingOutcomes.Favorable))
            return FindingOutcomes.Favorable;

        return FindingOutcomes.Unfavorable;
    }
}
