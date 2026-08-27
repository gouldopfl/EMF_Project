using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueDecisionReviewAnalysisServiceTests
{
    [Fact]
    public void Analyze_SelectsContributingTheoryForReview()
    {
        var issueId = new ClaimIssueId("issue-1");

        var favorable =
            CreateTheory(
                issueId,
                "theory-favorable",
                FindingOutcomes.Favorable);

        var unfavorable =
            CreateTheory(
                issueId,
                "theory-unfavorable",
                FindingOutcomes.Unfavorable);

        var merits =
            new ClaimIssueMeritsOutcomeAssessment
            {
                ClaimIssueId = issueId,
                TheoryOutcomes =
                    [unfavorable, favorable],
                Outcome = FindingOutcomes.Favorable
            };

        var review =
            CreateReview(
                issueId,
                requiresReview: true);

        var result =
            new ClaimIssueDecisionReviewAnalysisService()
                .Analyze(review, merits);

        var contributing =
            Assert.Single(result.ContributingTheoryOutcomes);

        Assert.Same(favorable, contributing);
    }

    [Fact]
    public void Analyze_ReturnsNoContributorsWithoutReview()
    {
        var issueId = new ClaimIssueId("issue-1");

        var merits =
            new ClaimIssueMeritsOutcomeAssessment
            {
                ClaimIssueId = issueId,
                TheoryOutcomes =
                [
                    CreateTheory(
                        issueId,
                        "theory-1",
                        FindingOutcomes.Favorable)
                ],
                Outcome = FindingOutcomes.Favorable
            };

        var result =
            new ClaimIssueDecisionReviewAnalysisService()
                .Analyze(
                    CreateReview(
                        issueId,
                        requiresReview: false),
                    merits);

        Assert.Empty(result.ContributingTheoryOutcomes);
    }

    [Fact]
    public void Analyze_RejectsClaimIssueMismatch()
    {
        var merits =
            new ClaimIssueMeritsOutcomeAssessment
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-1"),
                TheoryOutcomes = [],
                Outcome = FindingOutcomes.Unresolved
            };

        var review =
            CreateReview(
                new ClaimIssueId("issue-2"),
                requiresReview: true);

        Assert.Throws<InvalidOperationException>(
            () =>
                new ClaimIssueDecisionReviewAnalysisService()
                    .Analyze(review, merits));
    }

    private static ServiceConnectionTheoryOutcomeAssessment
        CreateTheory(
            ClaimIssueId issueId,
            string theoryId,
            string outcome)
    {
        return new ServiceConnectionTheoryOutcomeAssessment
        {
            Theory =
                new ServiceConnectionTheory
                {
                    Id =
                        new ServiceConnectionTheoryId(
                            theoryId),
                    ClaimIssueId = issueId,
                    TheoryType =
                        ServiceConnectionTheoryTypes.Direct
                },
            BasisOutcomes = [],
            Outcome = outcome
        };
    }

    private static ClaimIssueDecisionReview
        CreateReview(
            ClaimIssueId issueId,
            bool requiresReview)
    {
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
                Id =
                    new IssueDecisionId(
                        "issue-decision-1"),
                VaDecisionId =
                    new VaDecisionId("va-decision-1"),
                ClaimIssueId = issueId,
                Outcome = IssueDecisionOutcomes.Denied
            };

        return new ClaimIssueDecisionReview
        {
            ClaimIssueId = issueId,
            Comparison =
                new ClaimIssueDecisionComparison
                {
                    ClaimIssueId = issueId,
                    IssueDecision = decision,
                    Recommendation = recommendation,
                    ComparisonOutcome =
                        requiresReview
                            ? ClaimIssueDecisionComparisonOutcomes
                                .Disagreement
                            : ClaimIssueDecisionComparisonOutcomes
                                .Agreement
                },
            RequiresReview = requiresReview
        };
    }
}
