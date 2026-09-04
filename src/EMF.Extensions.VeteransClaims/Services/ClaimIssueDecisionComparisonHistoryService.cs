using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionComparisonHistoryService
{
    private readonly IVaDecisionRepository _decisions;
    private readonly ClaimIssueDecisionComparisonService _comparison;

    public ClaimIssueDecisionComparisonHistoryService(
        IVaDecisionRepository decisions,
        ClaimIssueDecisionComparisonService comparison)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(comparison);

        _decisions = decisions;
        _comparison = comparison;
    }

    public async Task<IReadOnlyList<ClaimIssueDecisionComparison>>
        GetAsync(
            ClaimIssueDecisionRecommendation recommendation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        var decisions =
            await _decisions.GetIssueDecisionsAsync(
                recommendation.ClaimIssueId,
                cancellationToken);

        var comparisons =
            new List<ClaimIssueDecisionComparison>(
                decisions.Count);

        foreach (var decision in decisions)
        {
            var comparison =
                _comparison.Compare(
                    recommendation,
                    decision);

            var vaDecision =
                await _decisions.GetDecisionAsync(
                    decision.VaDecisionId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "VA decision could not be read.");

            if (vaDecision.Id != decision.VaDecisionId)
                throw new InvalidOperationException(
                    "VA decision identity mismatch.");

            comparisons.Add(
                new ClaimIssueDecisionComparison
                {
                    ClaimIssueId = comparison.ClaimIssueId,
                    IssueDecision = comparison.IssueDecision,
                    VaDecision = vaDecision,
                    Recommendation = comparison.Recommendation,
                    ComparisonOutcome =
                        comparison.ComparisonOutcome
                });
        }

        return comparisons;
    }
}
