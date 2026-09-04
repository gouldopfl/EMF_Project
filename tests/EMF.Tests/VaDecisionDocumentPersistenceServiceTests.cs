using System.Reflection;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentPersistenceServiceTests
{
    [Fact]
    public async Task PersistAsync_RejectsMissingMatchedIssue()
    {
        var interpretation = CreateInterpretation();
        var request = new PersistVaDecisionDocumentRequest
        {
            VaDecisionId = new VaDecisionId("decision-1"),
            Interpretation = interpretation,
            MatchedIssues = []
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().PersistAsync(request));
    }

    [Fact]
    public async Task PersistAsync_RejectsContradictoryCandidate()
    {
        var interpretation = CreateInterpretation();
        var issue = interpretation.IssueDecisions.Single();
        var claimIssueId = new ClaimIssueId("issue-1");

        var request = new PersistVaDecisionDocumentRequest
        {
            VaDecisionId = new VaDecisionId("decision-1"),
            Interpretation = interpretation,
            MatchedIssues =
            [
                new VaDecisionDocumentMatchedIssue
                {
                    IssueDecisionId =
                        new IssueDecisionId("issue-decision-1"),
                    Match = new VaDecisionDocumentIssueMatch
                    {
                        Interpretation = issue,
                        Status =
                            VaDecisionDocumentIssueMatchStatuses.Matched,
                        ClaimIssueId = claimIssueId,
                        CandidateClaimIssueIds =
                        [
                            new ClaimIssueId("issue-other")
                        ]
                    }
                }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().PersistAsync(request));
    }

    [Fact]
    public async Task PersistAsync_RejectsDuplicateIssueDecisionIds()
    {
        var interpretation = CreateTwoIssueInterpretation();
        var issues = interpretation.IssueDecisions;
        var duplicateId =
            new IssueDecisionId("issue-decision-1");

        var request = new PersistVaDecisionDocumentRequest
        {
            VaDecisionId = new VaDecisionId("decision-1"),
            Interpretation = interpretation,
            MatchedIssues =
            [
                CreateMatchedIssue(
                    duplicateId,
                    issues[0],
                    new ClaimIssueId("issue-1")),
                CreateMatchedIssue(
                    duplicateId,
                    issues[1],
                    new ClaimIssueId("issue-2"))
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().PersistAsync(request));
    }

    [Fact]
    public async Task PersistAsync_RejectsForeignInterpretation()
    {
        var interpretation = CreateInterpretation();

        var foreign =
            CreateInterpretation()
                .IssueDecisions
                .Single();

        var request = new PersistVaDecisionDocumentRequest
        {
            VaDecisionId = new VaDecisionId("decision-1"),
            Interpretation = interpretation,
            MatchedIssues =
            [
                CreateMatchedIssue(
                    new IssueDecisionId("issue-decision-1"),
                    foreign,
                    new ClaimIssueId("issue-1"))
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().PersistAsync(request));
    }

    private static VaDecisionDocumentPersistenceService CreateService() =>
        new(
            DispatchProxy.Create<IVaDecisionRepository, RejectingProxy>(),
            new VaDecisionDocumentInterpretationValidator(),
            new VaDecisionDocumentIssueDecisionFactory());

    private static VaDecisionDocumentMatchedIssue
        CreateMatchedIssue(
            IssueDecisionId issueDecisionId,
            VaIssueDecisionInterpretation interpretation,
            ClaimIssueId claimIssueId) =>
        new()
        {
            IssueDecisionId = issueDecisionId,
            Match = new VaDecisionDocumentIssueMatch
            {
                Interpretation = interpretation,
                Status =
                    VaDecisionDocumentIssueMatchStatuses.Matched,
                ClaimIssueId = claimIssueId,
                CandidateClaimIssueIds = [claimIssueId]
            }
        };

    private static VaDecisionDocumentInterpretation
        CreateTwoIssueInterpretation()
    {
        var first = CreateInterpretation();

        return new VaDecisionDocumentInterpretation
        {
            ArtifactId = first.ArtifactId,
            DecisionDate = first.DecisionDate,
            IssueDecisions =
            [
                first.IssueDecisions.Single(),
                new VaIssueDecisionInterpretation
                {
                    IssueDescription = "GERD",
                    Outcome = IssueDecisionOutcomes.Denied,
                    Rationale = "Nexus was not established.",
                    FavorableFindings = [],
                    AdverseFindings = [],
                    CitedRegulations = [],
                    ReferencedEvidence = [],
                    SourceExcerpts =
                    [
                        new DecisionDocumentSourceExcerpt
                        {
                            ArtifactId = first.ArtifactId,
                            Text = "Decision text."
                        }
                    ]
                }
            ]
        };
    }

    private static VaDecisionDocumentInterpretation CreateInterpretation() =>
        new()
        {
            ArtifactId = new ArtifactId("artifact-1"),
            DecisionDate =
                new DateTimeOffset(
                    2026, 9, 4, 0, 0, 0,
                    TimeSpan.Zero),
            IssueDecisions =
            [
                new VaIssueDecisionInterpretation
                {
                    IssueDescription = "Sleep apnea",
                    Outcome = IssueDecisionOutcomes.Denied,
                    Rationale = "Nexus was not established.",
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
                            Text = "Decision text."
                        }
                    ]
                }
            ]
        };

    private class RejectingProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            throw new InvalidOperationException(
                "Repository must not be called.");
    }
}
