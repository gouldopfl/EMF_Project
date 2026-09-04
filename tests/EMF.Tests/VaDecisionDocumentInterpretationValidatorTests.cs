using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VaDecisionDocumentInterpretationValidatorTests
{
    [Fact]
    public void Validate_AcceptsDeniedIssueWithRationale()
    {
        var interpretation =
            CreateInterpretation(
                IssueDecisionOutcomes.Denied,
                "Nexus was not established.");

        new VaDecisionDocumentInterpretationValidator()
            .Validate(interpretation);
    }

    [Fact]
    public void Validate_RejectsUnknownOutcome()
    {
        var interpretation =
            CreateInterpretation(
                "UnknownOutcome",
                "Some rationale.");

        Assert.Throws<InvalidOperationException>(
            () =>
                new VaDecisionDocumentInterpretationValidator()
                    .Validate(interpretation));
    }

    [Fact]
    public void Validate_RejectsDeniedIssueWithoutRationale()
    {
        var interpretation =
            CreateInterpretation(
                IssueDecisionOutcomes.Denied,
                "");

        Assert.Throws<InvalidOperationException>(
            () =>
                new VaDecisionDocumentInterpretationValidator()
                    .Validate(interpretation));
    }

    [Fact]
    public void Validate_RejectsEmptySourceExcerpt()
    {
        var interpretation =
            new VaDecisionDocumentInterpretation
            {
                ArtifactId =
                    new ArtifactId("artifact-1"),
                DecisionDate = DateTimeOffset.UtcNow,
                IssueDecisions =
                [
                    new VaIssueDecisionInterpretation
                    {
                        IssueDescription =
                            "Service connection for sleep apnea",
                        Outcome =
                            IssueDecisionOutcomes.Denied,
                        Rationale =
                            "Nexus was not established.",
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
                                Text = ""
                            }
                        ]
                    }
                ]
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                new VaDecisionDocumentInterpretationValidator()
                    .Validate(interpretation));
    }

    [Fact]
    public void Validate_RejectsExcerptForDifferentArtifact()
    {
        var interpretation =
            CreateInterpretation(
                IssueDecisionOutcomes.Denied,
                "Nexus was not established.");

        var issue = interpretation.IssueDecisions.Single();

        var mismatched =
            new VaDecisionDocumentInterpretation
            {
                ArtifactId = interpretation.ArtifactId,
                DecisionDate = interpretation.DecisionDate,
                IssueDecisions =
                [
                    new VaIssueDecisionInterpretation
                    {
                        IssueDescription = issue.IssueDescription,
                        Outcome = issue.Outcome,
                        Rationale = issue.Rationale,
                        FavorableFindings = issue.FavorableFindings,
                        AdverseFindings = issue.AdverseFindings,
                        CitedRegulations = issue.CitedRegulations,
                        ReferencedEvidence = issue.ReferencedEvidence,
                        SourceExcerpts =
                        [
                            new DecisionDocumentSourceExcerpt
                            {
                                ArtifactId =
                                    new ArtifactId("artifact-other"),
                                Text = "Decision text."
                            }
                        ]
                    }
                ]
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                new VaDecisionDocumentInterpretationValidator()
                    .Validate(mismatched));
    }

    private static VaDecisionDocumentInterpretation
        CreateInterpretation(
            string outcome,
            string rationale)
    {
        return new VaDecisionDocumentInterpretation
        {
            ArtifactId = new ArtifactId("artifact-1"),
            DecisionDate = DateTimeOffset.UtcNow,
            IssueDecisions =
            [
                new VaIssueDecisionInterpretation
                {
                    IssueDescription =
                        "Service connection for sleep apnea",
                    Outcome = outcome,
                    Rationale = rationale,
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
                }
            ]
        };
    }
}
