using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueDecisionReviewServiceTests
{
    [Theory]
    [InlineData(
        ClaimIssueDecisionComparisonOutcomes.Agreement,
        false)]
    [InlineData(
        ClaimIssueDecisionComparisonOutcomes.Disagreement,
        true)]
    [InlineData(
        ClaimIssueDecisionComparisonOutcomes.NotComparable,
        false)]
    public void Assess_DerivesExpectedReviewRequirement(
        string comparisonOutcome,
        bool expectedRequiresReview)
    {
        var issueId = new ClaimIssueId("issue-1");

        var recommendation =
            new ClaimIssueDecisionRecommendation
            {
                ClaimIssueId = issueId,
                IsReadyForAdjudication = true,
                MeritsOutcome = FindingOutcomes.Favorable,
                RecommendedOutcome =
                    IssueDecisionOutcomes.Granted
            };

        var decision =
            new IssueDecision
            {
                Id = new IssueDecisionId("decision-1"),
                VaDecisionId = new VaDecisionId("va-1"),
                ClaimIssueId = issueId,
                Outcome = IssueDecisionOutcomes.Granted
            };

        var comparison =
            new ClaimIssueDecisionComparison
            {
                ClaimIssueId = issueId,
                IssueDecision = decision,
                Recommendation = recommendation,
                ComparisonOutcome = comparisonOutcome
            };

        var result =
            new ClaimIssueDecisionReviewService()
                .Assess(comparison);

        Assert.Equal(issueId, result.ClaimIssueId);
        Assert.Same(comparison, result.Comparison);
        Assert.Equal(
            expectedRequiresReview,
            result.RequiresReview);
    }

    [Fact]
    public void Assess_RejectsIssueDecisionClaimIssueMismatch()
    {
        var comparison =
            CreateComparison(
                new ClaimIssueId("issue-1"),
                new ClaimIssueId("issue-other"),
                new ClaimIssueId("issue-1"));

        Assert.Throws<InvalidOperationException>(
            () => new ClaimIssueDecisionReviewService()
                .Assess(comparison));
    }

    [Fact]
    public void Assess_RejectsRecommendationClaimIssueMismatch()
    {
        var comparison =
            CreateComparison(
                new ClaimIssueId("issue-1"),
                new ClaimIssueId("issue-1"),
                new ClaimIssueId("issue-other"));

        Assert.Throws<InvalidOperationException>(
            () => new ClaimIssueDecisionReviewService()
                .Assess(comparison));
    }

    private static ClaimIssueDecisionComparison CreateComparison(
        ClaimIssueId comparisonIssueId,
        ClaimIssueId decisionIssueId,
        ClaimIssueId recommendationIssueId) =>
        new()
        {
            ClaimIssueId = comparisonIssueId,
            IssueDecision =
                new IssueDecision
                {
                    Id = new IssueDecisionId("decision-test"),
                    VaDecisionId = new VaDecisionId("va-test"),
                    ClaimIssueId = decisionIssueId,
                    Outcome = IssueDecisionOutcomes.Denied
                },
            Recommendation =
                new ClaimIssueDecisionRecommendation
                {
                    ClaimIssueId = recommendationIssueId,
                    IsReadyForAdjudication = true,
                    MeritsOutcome = FindingOutcomes.Favorable,
                    RecommendedOutcome =
                        IssueDecisionOutcomes.Granted
                },
            ComparisonOutcome =
                ClaimIssueDecisionComparisonOutcomes.Disagreement
        };
}
