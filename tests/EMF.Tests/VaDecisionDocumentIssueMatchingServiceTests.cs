using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentIssueMatchingServiceTests
{
    [Fact]
    public void Match_ReturnsMixedDocumentResults()
    {
        var sleepIssue =
            new ClaimIssueId("issue-sleep");

        var duplicateA =
            new ClaimIssueId("issue-knee-a");

        var duplicateB =
            new ClaimIssueId("issue-knee-b");

        var interpretation =
            new VaDecisionDocumentInterpretation
            {
                ArtifactId =
                    new ArtifactId("artifact-1"),
                DecisionDate =
                    new DateTimeOffset(
                        2026, 8, 27,
                        0, 0, 0,
                        TimeSpan.Zero),
                IssueDecisions =
                [
                    CreateIssue("Sleep apnea"),
                    CreateIssue("GERD"),
                    CreateIssue("Left knee")
                ]
            };

        var conditions =
            new ClaimedCondition[]
            {
                CreateCondition(
                    "c1",
                    sleepIssue,
                    "Sleep apnea"),
                CreateCondition(
                    "c2",
                    duplicateA,
                    "Left knee"),
                CreateCondition(
                    "c3",
                    duplicateB,
                    "Left knee")
            };

        var result =
            new VaDecisionDocumentIssueMatchingService(
                new VaDecisionDocumentIssueMatcher())
            .Match(
                interpretation,
                conditions);

        Assert.Equal(3, result.Count);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Matched,
            result[0].Status);
        Assert.Equal(
            sleepIssue,
            result[0].ClaimIssueId);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Unmatched,
            result[1].Status);
        Assert.Null(result[1].ClaimIssueId);

        Assert.Equal(
            VaDecisionDocumentIssueMatchStatuses.Ambiguous,
            result[2].Status);
        Assert.Null(result[2].ClaimIssueId);
        Assert.Equal(
            2,
            result[2].CandidateClaimIssueIds.Count);
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
            Rationale = "Rationale.",
            FavorableFindings = [],
            AdverseFindings = [],
            CitedRegulations = [],
            ReferencedEvidence = [],
            SourceExcerpts =
            [
                new DecisionDocumentSourceExcerpt
                {
                    ArtifactId =
                        new ArtifactId("artifact-1"),
                    Text =
                        "Decision text."
                }
            ]
        };
}
