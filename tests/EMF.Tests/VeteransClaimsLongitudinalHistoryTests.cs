using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsLongitudinalHistoryTests
{
    [Fact]
    public void ClaimIssue_PersistsAcrossProceduralHistory()
    {
        var claimIssueId =
            new ClaimIssueId("claim-issue-001");

        var initialSubmissionId =
            new SubmissionId("submission-001");

        var supplementalSubmissionId =
            new SubmissionId("submission-002");

        var submissionHistory = new[]
        {
            new SubmissionClaimIssue
            {
                SubmissionId = initialSubmissionId,
                ClaimIssueId = claimIssueId
            },
            new SubmissionClaimIssue
            {
                SubmissionId = supplementalSubmissionId,
                ClaimIssueId = claimIssueId
            }
        };

        var initialDecisionId =
            new IssueDecisionId("issue-decision-001");

        var supplementalDecisionId =
            new IssueDecisionId("issue-decision-002");

        var issueDecisions = new[]
        {
            new IssueDecision
            {
                Id = initialDecisionId,
                VaDecisionId =
                    new VaDecisionId("decision-001"),
                ClaimIssueId = claimIssueId,
                Outcome = IssueDecisionOutcomes.Granted
            },
            new IssueDecision
            {
                Id = supplementalDecisionId,
                VaDecisionId =
                    new VaDecisionId("decision-002"),
                ClaimIssueId = claimIssueId,
                Outcome = IssueDecisionOutcomes.Granted
            }
        };

        var decisionHistory = new[]
        {
            new IssueDecisionSubmission
            {
                IssueDecisionId = initialDecisionId,
                SubmissionId = initialSubmissionId
            },
            new IssueDecisionSubmission
            {
                IssueDecisionId = supplementalDecisionId,
                SubmissionId = supplementalSubmissionId
            }
        };

        var evaluations = new[]
        {
            new DisabilityEvaluation
            {
                Id =
                    new DisabilityEvaluationId(
                        "evaluation-001"),
                IssueDecisionId = initialDecisionId,
                Evaluation = "30%"
            },
            new DisabilityEvaluation
            {
                Id =
                    new DisabilityEvaluationId(
                        "evaluation-002"),
                IssueDecisionId = supplementalDecisionId,
                Evaluation = "50%"
            }
        };

        var effectiveDates = new[]
        {
            new EffectiveDate
            {
                Id =
                    new EffectiveDateId(
                        "effective-date-001"),
                DisabilityEvaluationId =
                    evaluations[0].Id,
                Date = new DateOnly(2024, 1, 1)
            },
            new EffectiveDate
            {
                Id =
                    new EffectiveDateId(
                        "effective-date-002"),
                DisabilityEvaluationId =
                    evaluations[1].Id,
                Date = new DateOnly(2026, 1, 1)
            }
        };

        Assert.All(
            submissionHistory,
            item => Assert.Equal(
                claimIssueId,
                item.ClaimIssueId));

        Assert.All(
            issueDecisions,
            item => Assert.Equal(
                claimIssueId,
                item.ClaimIssueId));

        Assert.Equal(
            initialSubmissionId,
            decisionHistory[0].SubmissionId);

        Assert.Equal(
            supplementalSubmissionId,
            decisionHistory[1].SubmissionId);

        Assert.Equal(
            initialDecisionId,
            evaluations[0].IssueDecisionId);

        Assert.Equal(
            supplementalDecisionId,
            evaluations[1].IssueDecisionId);

        Assert.Equal(
            evaluations[0].Id,
            effectiveDates[0].DisabilityEvaluationId);

        Assert.Equal(
            evaluations[1].Id,
            effectiveDates[1].DisabilityEvaluationId);
    }
}
