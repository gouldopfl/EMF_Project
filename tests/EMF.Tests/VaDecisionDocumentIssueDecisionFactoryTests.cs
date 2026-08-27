using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentIssueDecisionFactoryTests
{
    [Fact]
    public void Create_BuildsIssueDecisionFromUniqueMatch()
    {
        var claimIssueId = new ClaimIssueId("issue-1");

        var result =
            new VaDecisionDocumentIssueDecisionFactory()
                .Create(
                    new IssueDecisionId("issue-decision-1"),
                    new VaDecisionId("va-decision-1"),
                    CreateMatch(
                        VaDecisionDocumentIssueMatchStatuses.Matched,
                        claimIssueId,
                        [claimIssueId]));

        Assert.Equal(
            new IssueDecisionId("issue-decision-1"),
            result.Id);
        Assert.Equal(
            new VaDecisionId("va-decision-1"),
            result.VaDecisionId);
        Assert.Equal(claimIssueId, result.ClaimIssueId);
        Assert.Equal(
            IssueDecisionOutcomes.Denied,
            result.Outcome);
    }

    [Fact]
    public void Create_RejectsUnmatchedIssue()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                new VaDecisionDocumentIssueDecisionFactory()
                    .Create(
                        new IssueDecisionId("issue-decision-1"),
                        new VaDecisionId("va-decision-1"),
                        CreateMatch(
                            VaDecisionDocumentIssueMatchStatuses.Unmatched,
                            null,
                            [])));
    }

    [Fact]
    public void Create_RejectsAmbiguousIssue()
    {
        var first = new ClaimIssueId("issue-1");
        var second = new ClaimIssueId("issue-2");

        Assert.Throws<InvalidOperationException>(
            () =>
                new VaDecisionDocumentIssueDecisionFactory()
                    .Create(
                        new IssueDecisionId("issue-decision-1"),
                        new VaDecisionId("va-decision-1"),
                        CreateMatch(
                            VaDecisionDocumentIssueMatchStatuses.Ambiguous,
                            null,
                            [first, second])));
    }

    private static VaDecisionDocumentIssueMatch CreateMatch(
        string status,
        ClaimIssueId? claimIssueId,
        IReadOnlyList<ClaimIssueId> candidates) =>
        new()
        {
            Interpretation =
                new VaIssueDecisionInterpretation
                {
                    IssueDescription = "Sleep apnea",
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
                            ArtifactId =
                                new ArtifactId("artifact-1"),
                            Text =
                                "Service connection is denied."
                        }
                    ]
                },
            Status = status,
            ClaimIssueId = claimIssueId,
            CandidateClaimIssueIds = candidates
        };
}
