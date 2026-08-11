using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsStagedDisabilityEvaluationTests
{
    [Fact]
    public void IssueDecision_SupportsStagedDisabilityEvaluations()
    {
        var claimIssueId =
            new ClaimIssueId("claim-issue-001");

        var issueDecisionId =
            new IssueDecisionId("issue-decision-001");

        var issueDecision = new IssueDecision
        {
            Id = issueDecisionId,
            VaDecisionId =
                new VaDecisionId("decision-001"),
            ClaimIssueId = claimIssueId,
            Outcome = IssueDecisionOutcomes.Granted
        };

        var initialEvaluation = new DisabilityEvaluation
        {
            Id =
                new DisabilityEvaluationId(
                    "evaluation-001"),
            IssueDecisionId = issueDecisionId,
            Evaluation = "0%"
        };

        var increasedEvaluation = new DisabilityEvaluation
        {
            Id =
                new DisabilityEvaluationId(
                    "evaluation-002"),
            IssueDecisionId = issueDecisionId,
            Evaluation = "50%"
        };

        var initialEffectiveDate = new EffectiveDate
        {
            Id =
                new EffectiveDateId(
                    "effective-date-001"),
            DisabilityEvaluationId =
                initialEvaluation.Id,
            Date = new DateOnly(2024, 1, 1)
        };

        var increasedEffectiveDate = new EffectiveDate
        {
            Id =
                new EffectiveDateId(
                    "effective-date-002"),
            DisabilityEvaluationId =
                increasedEvaluation.Id,
            Date = new DateOnly(2026, 1, 1)
        };

        Assert.Equal(
            claimIssueId,
            issueDecision.ClaimIssueId);

        Assert.Equal(
            issueDecisionId,
            initialEvaluation.IssueDecisionId);

        Assert.Equal(
            issueDecisionId,
            increasedEvaluation.IssueDecisionId);

        Assert.Equal(
            "0%",
            initialEvaluation.Evaluation);

        Assert.Equal(
            "50%",
            increasedEvaluation.Evaluation);

        Assert.Equal(
            initialEvaluation.Id,
            initialEffectiveDate.DisabilityEvaluationId);

        Assert.Equal(
            increasedEvaluation.Id,
            increasedEffectiveDate.DisabilityEvaluationId);

        Assert.True(
            initialEffectiveDate.Date <
            increasedEffectiveDate.Date);
    }
}
