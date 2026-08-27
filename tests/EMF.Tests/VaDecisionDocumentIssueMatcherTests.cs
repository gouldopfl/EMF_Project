using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentIssueMatcherTests
{
    [Fact]
    public void Match_ResolvesNormalizedName()
    {
        var id = new ClaimIssueId("issue-1");

        var result = new VaDecisionDocumentIssueMatcher().Match(
            CreateIssue("  sleep   apnea "),
            [CreateCondition("c1", id, "Sleep Apnea")]);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Matched,
            result.Status);
        Assert.Equal(id, result.ClaimIssueId);
        Assert.Single(result.CandidateClaimIssueIds);
    }

    [Fact]
    public void Match_ReturnsUnmatched()
    {
        var result = new VaDecisionDocumentIssueMatcher().Match(
            CreateIssue("Sleep apnea"),
            [CreateCondition(
                "c1",
                new ClaimIssueId("issue-1"),
                "GERD")]);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Unmatched,
            result.Status);
        Assert.Null(result.ClaimIssueId);
        Assert.Empty(result.CandidateClaimIssueIds);
    }

    [Fact]
    public void Match_ReturnsAmbiguous()
    {
        var result = new VaDecisionDocumentIssueMatcher().Match(
            CreateIssue("Sleep apnea"),
            [
                CreateCondition(
                    "c1",
                    new ClaimIssueId("issue-1"),
                    "Sleep apnea"),
                CreateCondition(
                    "c2",
                    new ClaimIssueId("issue-2"),
                    "Sleep apnea")
            ]);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Ambiguous,
            result.Status);
        Assert.Null(result.ClaimIssueId);
        Assert.Equal(2, result.CandidateClaimIssueIds.Count);
    }

    private static ClaimedCondition CreateCondition(
        string id,
        ClaimIssueId issueId,
        string name) =>
        new()
        {
            Id = new ClaimedConditionId(id),
            ClaimIssueId = issueId,
            Name = name
        };

    private static VaIssueDecisionInterpretation CreateIssue(
        string description) =>
        new()
        {
            IssueDescription = description,
            Outcome = IssueDecisionOutcomes.Denied,
            Rationale = "Nexus not established.",
            FavorableFindings = [],
            AdverseFindings = [],
            CitedRegulations = [],
            ReferencedEvidence = [],
            SourceExcerpts =
            [
                new DecisionDocumentSourceExcerpt
                {
                    ArtifactId = new ArtifactId("artifact-1"),
                    Text = "Service connection is denied."
                }
            ]
        };
}
