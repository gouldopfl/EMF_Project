using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueDecisionComparisonServiceTests
{
    [Fact]
    public void Compare_ReturnsAgreementWhenOutcomesMatch()
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

        var result =
            new ClaimIssueDecisionComparisonService()
                .Compare(recommendation, decision);

        Assert.Equal(
            ClaimIssueDecisionComparisonOutcomes.Agreement,
            result.ComparisonOutcome);
    }

    [Fact]
    public void Compare_ReturnsDisagreementWhenOutcomesDiffer()
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
                Outcome = IssueDecisionOutcomes.Denied
            };

        var result =
            new ClaimIssueDecisionComparisonService()
                .Compare(recommendation, decision);

        Assert.Equal(
            ClaimIssueDecisionComparisonOutcomes.Disagreement,
            result.ComparisonOutcome);
    }

    [Fact]
    public void Compare_ReturnsNotComparableWithoutRecommendation()
    {
        var issueId = new ClaimIssueId("issue-1");

        var recommendation =
            new ClaimIssueDecisionRecommendation
            {
                ClaimIssueId = issueId,
                IsReadyForAdjudication = true,
                MeritsOutcome = FindingOutcomes.Unresolved,
                RecommendedOutcome = null
            };

        var decision =
            new IssueDecision
            {
                Id = new IssueDecisionId("decision-1"),
                VaDecisionId = new VaDecisionId("va-1"),
                ClaimIssueId = issueId,
                Outcome = IssueDecisionOutcomes.Denied
            };

        var result =
            new ClaimIssueDecisionComparisonService()
                .Compare(recommendation, decision);

        Assert.Equal(
            ClaimIssueDecisionComparisonOutcomes.NotComparable,
            result.ComparisonOutcome);
    }

    [Fact]
    public void Compare_RejectsClaimIssueMismatch()
    {
        var recommendation =
            new ClaimIssueDecisionRecommendation
            {
                ClaimIssueId = new ClaimIssueId("issue-1"),
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
                ClaimIssueId = new ClaimIssueId("issue-2"),
                Outcome = IssueDecisionOutcomes.Granted
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                new ClaimIssueDecisionComparisonService()
                    .Compare(recommendation, decision));
    }
}
