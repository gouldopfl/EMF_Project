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

        return decisions
            .Select(
                decision =>
                    _comparison.Compare(
                        recommendation,
                        decision))
            .ToArray();
    }
}
