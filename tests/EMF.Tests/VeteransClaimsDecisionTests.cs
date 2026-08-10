using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsDecisionTests
{
    [Fact]
    public void DecisionChain_PreservesIssueEvaluationAndEffectiveDate()
    {
        var vaDecisionId = new VaDecisionId("decision-001");
        var issueDecisionId = new IssueDecisionId("issue-decision-001");
        var claimIssueId = new ClaimIssueId("claim-issue-001");
        var evaluationId = new DisabilityEvaluationId("evaluation-001");

        var decision = new VaDecision
        {
            Id = vaDecisionId,
            DecisionDate = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero)
        };

        var issueDecision = new IssueDecision
        {
            Id = issueDecisionId,
            VaDecisionId = vaDecisionId,
            ClaimIssueId = claimIssueId,
            Outcome = IssueDecisionOutcomes.Granted
        };

        var evaluation = new DisabilityEvaluation
        {
            Id = evaluationId,
            IssueDecisionId = issueDecisionId,
            Evaluation = "50%"
        };

        var effectiveDate = new EffectiveDate
        {
            Id = new EffectiveDateId("effective-date-001"),
            DisabilityEvaluationId = evaluationId,
            Date = new DateOnly(2026, 1, 1)
        };

        Assert.Equal(vaDecisionId, decision.Id);
        Assert.Equal(vaDecisionId, issueDecision.VaDecisionId);
        Assert.Equal(claimIssueId, issueDecision.ClaimIssueId);
        Assert.Equal(IssueDecisionOutcomes.Granted, issueDecision.Outcome);
        Assert.Equal(issueDecisionId, evaluation.IssueDecisionId);
        Assert.Equal("50%", evaluation.Evaluation);
        Assert.Equal(evaluationId, effectiveDate.DisabilityEvaluationId);
        Assert.Equal(new DateOnly(2026, 1, 1), effectiveDate.Date);
    }
}
