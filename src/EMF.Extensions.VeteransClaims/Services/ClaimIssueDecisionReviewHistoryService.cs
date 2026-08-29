using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueDecisionReviewHistoryService
{
    private readonly ClaimIssueDecisionComparisonHistoryService
        _comparisons;

    private readonly ClaimIssueDecisionReviewService
        _reviews;

    private readonly ClaimIssueDecisionReviewAnalysisService
        _analysis;

    public ClaimIssueDecisionReviewHistoryService(
        ClaimIssueDecisionComparisonHistoryService comparisons,
        ClaimIssueDecisionReviewService reviews,
        ClaimIssueDecisionReviewAnalysisService analysis)
    {
        ArgumentNullException.ThrowIfNull(comparisons);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(analysis);

        _comparisons = comparisons;
        _reviews = reviews;
        _analysis = analysis;
    }

    public async Task<
        IReadOnlyList<ClaimIssueDecisionReviewAnalysis>>
        GetAsync(
            ClaimIssueDecisionRecommendation recommendation,
            ClaimIssueMeritsOutcomeAssessment merits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(merits);

        var comparisons =
            await _comparisons.GetAsync(
                recommendation,
                cancellationToken);

        return comparisons
            .Select(
                comparison =>
                    _analysis.Analyze(
                        _reviews.Assess(comparison),
                        merits))
            .ToArray();
    }
}
