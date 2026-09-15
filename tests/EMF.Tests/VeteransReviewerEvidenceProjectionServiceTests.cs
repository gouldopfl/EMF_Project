using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class VeteransReviewerEvidenceProjectionServiceTests
{
    [Fact]
    public async Task ProjectAsync_UsesClaimTermsWithOneLineContext()
    {
        var repository =
            new InMemoryEvidenceRecognitionTermRepository();

        var service =
            new VeteransReviewerEvidenceProjectionService(
                repository);

        var details = CreateDetails();
        var source =
            Source(
                "artifact-claim-context",
                "Ignore first\n" +
                "Context before diagnosis\n" +
                "Obstructive sleep apnea was diagnosed.\n" +
                "Context after diagnosis\n" +
                "Ignore middle\n" +
                "Ignore last");

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    details,
                    [source]));

        Assert.Equal(source.ArtifactId, projection.ArtifactId);
        Assert.Equal(
            [2, 3, 4],
            projection.SourceLineNumbers);
        Assert.Equal(
            "Context before diagnosis\n" +
            "Obstructive sleep apnea was diagnosed.\n" +
            "Context after diagnosis",
            projection.Text);
        Assert.Contains("apnea", projection.MatchedTerms);
        Assert.Contains("Obstructive", projection.MatchedTerms);
        Assert.Contains("sleep", projection.MatchedTerms);
    }

    [Fact]
    public async Task ProjectAsync_UsesRequirementRecognitionTerms()
    {
        var repository =
            new InMemoryEvidenceRecognitionTermRepository();

        var details = CreateDetails(includeRequirement: true);
        var requirement = Assert.Single(details.Requirements);

        await repository.AddEvidenceRecognitionTermAsync(
            new EvidenceRecognitionTerm
            {
                Id = new EvidenceRecognitionTermId("term-cpap"),
                RequirementId = requirement.Requirement.Id,
                Term = "CPAP",
                TermType = EvidenceRecognitionTermTypes.Acronym,
                RecognitionRole = EvidenceRecognitionRoles.EvidenceType,
                EvidenceClassification =
                    EvidenceClassifications.MedicalEvidence,
                AuthoritySource = "test"
            });

        var service =
            new VeteransReviewerEvidenceProjectionService(
                repository);

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    details,
                    [
                        Source(
                            "artifact-requirement-term",
                            "Unrelated\n" +
                            "PAP context\n" +
                            "CPAP use remains documented.\n" +
                            "Follow-up context\n" +
                            "Unrelated end")
                    ]));

        Assert.Equal([2, 3, 4], projection.SourceLineNumbers);
        Assert.Contains("CPAP", projection.MatchedTerms);
    }

    [Fact]
    public async Task ProjectAsync_DeduplicatesNormalizedLinesExactly()
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    CreateDetails(),
                    [
                        Source(
                            "artifact-dedup",
                            "Shared   context\n" +
                            "sleep apnea noted\n" +
                            "Unique first context\n" +
                            "Ignore\n" +
                            "Shared context\n" +
                            "OSA remains active\n" +
                            "Unique second context")
                    ]));

        Assert.Equal(
            [1, 2, 3, 6, 7],
            projection.SourceLineNumbers);
        Assert.Equal(
            "Shared context\n" +
            "sleep apnea noted\n" +
            "Unique first context\n" +
            "OSA remains active\n" +
            "Unique second context",
            projection.Text);
    }

    [Fact]
    public async Task ProjectAsync_NoMatchReturnsEmptyProjectionWithLineage()
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var source =
            Source(
                "artifact-no-match",
                "Completely unrelated evidence text.");

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    CreateDetails(),
                    [source]));

        Assert.Equal(source.ArtifactId, projection.ArtifactId);
        Assert.Equal(string.Empty, projection.Text);
        Assert.Empty(projection.SourceLineNumbers);
        Assert.Empty(projection.MatchedTerms);
    }

    [Fact]
    public async Task ProjectAsync_PreservesDeterministicArtifactAndLineOrder()
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var sources = new[]
        {
            Source(
                "artifact-b",
                "before B\nOSA B\nafter B"),
            Source(
                "artifact-a",
                "before A\nsleep apnea A\nafter A")
        };

        var first =
            await service.ProjectAsync(
                CreateDetails(),
                sources);
        var second =
            await service.ProjectAsync(
                CreateDetails(),
                sources);

        Assert.Equal(
            sources.Select(source => source.ArtifactId),
            first.Select(item => item.ArtifactId));
        Assert.Equal(
            first.Select(item => item.Text),
            second.Select(item => item.Text));
        Assert.All(
            first,
            projection =>
                Assert.Equal(
                    [1, 2, 3],
                    projection.SourceLineNumbers));
    }

    [Fact]
    public async Task ProjectAsync_RejectsDuplicateArtifactLineage()
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var source =
            Source(
                "artifact-duplicate",
                "OSA evidence");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ProjectAsync(
                    CreateDetails(),
                    [source, source]));
    }

    [Fact]
    public async Task ProjectAsync_UsesSecondaryConditionAndMedicationTerms()
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var details = CreateDetails(includeSecondaryContext: true);

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    details,
                    [
                        Source(
                            "artifact-secondary",
                            "Context one\n" +
                            "PTSD symptoms documented.\n" +
                            "Context two\n" +
                            "Ignore\n" +
                            "Context three\n" +
                            "Trazodone continued.\n" +
                            "Context four")
                    ]));

        Assert.Equal(
            [1, 2, 3, 5, 6, 7],
            projection.SourceLineNumbers);
        Assert.Contains("PTSD", projection.MatchedTerms);
        Assert.Contains("Trazodone", projection.MatchedTerms);
    }

    [Theory]
    [InlineData(EvidenceClassifications.LayEvidence)]
    [InlineData(EvidenceClassifications.MedicalOpinion)]
    public async Task ProjectAsync_PreservesCriticalClassificationsWhenTermsDoNotMatch(
        string classification)
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var source =
            new VeteransReviewerEvidenceSource
            {
                ArtifactId = new ArtifactId("artifact-critical-fallback"),
                Classifications = [classification],
                Text =
                    "First factual line\n" +
                    "Second factual line\n" +
                    "Second   factual   line\n" +
                    "Third factual line"
            };

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    CreateDetails(),
                    [source]));

        Assert.Equal(
            "First factual line\n" +
            "Second factual line\n" +
            "Third factual line",
            projection.Text);
        Assert.Equal([1, 2, 4], projection.SourceLineNumbers);
        Assert.Empty(projection.MatchedTerms);
    }

    [Theory]
    [InlineData(EvidenceClassifications.LayEvidence)]
    [InlineData(EvidenceClassifications.MedicalOpinion)]
    public async Task ProjectAsync_BoundsClassificationPreservingFallback(
        string classification)
    {
        var service =
            new VeteransReviewerEvidenceProjectionService(
                new InMemoryEvidenceRecognitionTermRepository());

        var sourceText =
            string.Join(
                "\n",
                Enumerable.Range(0, 2_000)
                    .Select(
                        index =>
                            $"Factual line {index:D4} " +
                            new string('x', 40)));

        var source =
            new VeteransReviewerEvidenceSource
            {
                ArtifactId = new ArtifactId("artifact-bounded-fallback"),
                Classifications = [classification],
                Text = sourceText
            };

        var projection =
            Assert.Single(
                await service.ProjectAsync(
                    CreateDetails(),
                    [source]));

        Assert.NotEmpty(projection.Text);
        Assert.True(projection.Text.Length <= 24_000);
        Assert.True(projection.Text.Length < sourceText.Length);
        Assert.NotEmpty(projection.SourceLineNumbers);
        Assert.Empty(projection.MatchedTerms);
    }

    private static ClaimIssueAdjudicationDetails CreateDetails(
        bool includeRequirement = false,
        bool includeSecondaryContext = false)
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-projection"),
            ClaimId = new ClaimId("claim-projection"),
            ClaimIssueType = "ServiceConnection"
        };

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-secondary"),
                ClaimIssueId = issue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-secondary"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

        var claimedCondition = new ClaimedCondition
        {
            Id = new ClaimedConditionId("condition-osa"),
            ClaimIssueId = issue.Id,
            Name = "Obstructive sleep apnea (OSA)"
        };

        var requirements =
            includeRequirement
                ? new[] { Requirement(basis) }
                : [];

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [claimedCondition],
            ClaimedConditionBases =
            [
                new ServiceConnectionBasisClaimedConditionDetails
                {
                    Basis = basis,
                    ClaimedCondition = claimedCondition
                }
            ],
            ServiceConnectionTheories = [theory],
            ServiceConnectionBases = [basis],
            ServiceConnectedConditions =
                includeSecondaryContext
                    ?
                    [
                        new ServiceConnectionBasisConditionDetails
                        {
                            Basis = basis,
                            ServiceConnectedCondition =
                                new MedicalCondition
                                {
                                    Id = new MedicalConditionId("condition-ptsd"),
                                    Name = "PTSD"
                                }
                        }
                    ]
                    : [],
            PrescribedMedications =
                includeSecondaryContext
                    ?
                    [
                        new ServiceConnectionBasisMedicationDetails
                        {
                            Basis = basis,
                            MedicationName = "Trazodone"
                        }
                    ]
                    : [],
            ServiceEvents = [],
            Requirements = requirements,
            Evidence =
                new ClaimIssueEvidenceDetails
                {
                    ClaimIssue = issue,
                    Checklist =
                        new ClaimIssueEvidenceChecklist
                        {
                            ClaimIssueId = issue.Id,
                            RequirementChecklists = []
                        },
                    DevelopmentPlans = []
                },
            Timeline = []
        };
    }

    private static ServiceConnectionBasisRequirementDetails Requirement(
        ServiceConnectionBasis basis)
    {
        var requirementId =
            new RequirementId("requirement-projection");
        var provisionId =
            new RegulatoryProvisionId("provision-projection");

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis = basis,
            Requirement =
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provisionId,
                    Description = "Test requirement"
                },
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id = provisionId,
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId("authority-test"),
                    ProvisionType = RegulatoryProvisionTypes.Requirement,
                    Citation = "Test citation"
                },
            Responsiveness =
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = requirementId,
                    Items = []
                },
            DevelopmentChecklist =
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items = []
                }
        };
    }

    private static VeteransReviewerEvidenceSource Source(
        string artifactId,
        string text) =>
        new()
        {
            ArtifactId = new ArtifactId(artifactId),
            Classifications = [],
            Text = text
        };
}
