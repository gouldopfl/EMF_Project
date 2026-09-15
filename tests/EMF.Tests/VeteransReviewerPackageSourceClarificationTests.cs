using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageSourceClarificationTests
{
    [Fact]
    public async Task GetAsync_AttachesClarificationToContainingDerivedRecordOnly()
    {
        var sourceId = new ArtifactId("blue-button");
        var affectedId = new ArtifactId("sleep-note");
        var unrelatedId = new ArtifactId("other-note");
        var packageId = new EvidencePackageId("package-1");
        var issueId = new ClaimIssueId("issue-osa");

        var details =
            Details(
                packageId,
                issueId,
                Content(affectedId, sourceId, 2010, 2017),
                Content(unrelatedId, sourceId, 2500, 2502));

        var repository =
            new SourceClarificationRepositoryStub(
                new SourceClarification
                {
                    Id = new SourceClarificationId("clarification-1"),
                    ClaimIssueId = issueId,
                    SourceArtifactId = sourceId,
                    EvidenceDate = new DateOnly(2024, 1, 25),
                    SourceStartPage = 2010,
                    SourceEndPage = 2010,
                    RecordTitle =
                        "Sleep Medicine Specialist Initial Consultation Note",
                    Category = SourceClarificationCategories.ImpossibleMagnitude,
                    OriginalText =
                        "After his bariatric surgery he lost about 8200 pounds.",
                    Clarification =
                        "Source clarification: The source record states \"8200 pounds.\" " +
                        "The Veteran reports the intended value is 82 pounds. " +
                        "Original source text is preserved below."
                });

        var projected =
            await new VeteransReviewerPackageSourceClarificationService(repository)
                .GetAsync(details);

        var clarification = Assert.Single(projected);

        Assert.Equal(affectedId, clarification.ReviewerArtifactId);
        Assert.Equal(
            "VA Blue Button Report — Sleep Medicine Specialist Initial Consultation Note — January 25, 2024",
            clarification.SourceLocator);
        Assert.Contains("8200 pounds", clarification.OriginalText);
        Assert.Contains("82 pounds", clarification.Clarification);

        var json = JsonSerializer.Serialize(clarification);
        Assert.DoesNotContain("sleep-note", json);
        Assert.DoesNotContain("blue-button", json);
        Assert.DoesNotContain("2010", json);
    }

    [Fact]
    public async Task GetAsync_DoesNotAttachClarificationOutsideDerivedSourceRange()
    {
        var sourceId = new ArtifactId("blue-button");
        var issueId = new ClaimIssueId("issue-osa");

        var details =
            Details(
                new EvidencePackageId("package-1"),
                issueId,
                Content(new ArtifactId("other-note"), sourceId, 2500, 2502));

        var repository =
            new SourceClarificationRepositoryStub(
                Clarification(issueId, sourceId, 2010, 2010));

        var projected =
            await new VeteransReviewerPackageSourceClarificationService(repository)
                .GetAsync(details);

        Assert.Empty(projected);
    }

    [Fact]
    public void Render_ShowsClarificationAdjacentToAffectedEvidenceWithoutInternalProvenance()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifactId = new ArtifactId("sleep-note");
        var issueId = new ClaimIssueId("issue-osa");
        var content =
            Content(
                artifactId,
                new ArtifactId("blue-button"),
                2010,
                2017,
                printableText:
                    "After his bariatric surgery he lost about 8200 pounds.");

        var details =
            Details(
                packageId,
                issueId,
                content);

        details =
            new VeteransReviewerPackageDetails
            {
                PackageDetails = details.PackageDetails,
                Artifacts = details.Artifacts,
                ArtifactContents = details.ArtifactContents,
                SourceClarifications =
                [
                    new VeteransReviewerSourceClarification
                    {
                        ReviewerArtifactId = artifactId,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Medicine Specialist Initial Consultation Note — January 25, 2024",
                        OriginalText =
                            "After his bariatric surgery he lost about 8200 pounds.",
                        Clarification =
                            "Source clarification: The source record states \"8200 pounds.\" " +
                            "The Veteran reports the intended value is 82 pounds. " +
                            "Original source text is preserved below."
                    }
                ]
            };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;

        var clarificationIndex =
            text.IndexOf("Source Clarification", StringComparison.Ordinal);
        var sourcePageIndex =
            text.IndexOf("Source Page 1", StringComparison.Ordinal);

        Assert.True(clarificationIndex >= 0);
        Assert.True(sourcePageIndex > clarificationIndex);
        Assert.Contains("The Veteran reports the intended value is 82 pounds", text);
        Assert.Contains("Source text: After his bariatric surgery he lost about 8200 pounds.", text);
        Assert.DoesNotContain("2010", text);
        Assert.DoesNotContain("blue-button", text);
        Assert.DoesNotContain("clarification-1", text);
    }

    private static SourceClarification Clarification(
        ClaimIssueId issueId,
        ArtifactId sourceId,
        int startPage,
        int endPage) =>
        new()
        {
            Id = new SourceClarificationId("clarification-1"),
            ClaimIssueId = issueId,
            SourceArtifactId = sourceId,
            EvidenceDate = new DateOnly(2024, 1, 25),
            SourceStartPage = startPage,
            SourceEndPage = endPage,
            RecordTitle = "Sleep Medicine Specialist Initial Consultation Note",
            Category = SourceClarificationCategories.ImpossibleMagnitude,
            OriginalText = "8200 pounds",
            Clarification = "The Veteran reports the intended value is 82 pounds."
        };

    private static VeteransReviewerPackageDetails Details(
        EvidencePackageId packageId,
        ClaimIssueId issueId,
        params VeteransReviewerArtifactContent[] contents) =>
        new()
        {
            PackageDetails =
                new EvidencePackageDetails
                {
                    Package =
                        new EvidencePackage
                        {
                            Id = packageId,
                            ClaimIssueId = issueId,
                            Purpose = "Physician reviewer package",
                            ReviewerRole = "MedicalProfessional"
                        },
                    Artifacts =
                        contents
                            .Select(
                                content =>
                                    new EvidencePackageArtifact
                                    {
                                        EvidencePackageId = packageId,
                                        ArtifactId = content.Artifact.Id,
                                        ContentRole =
                                            EvidencePackageContentRoles.UnderlyingEvidence
                                    })
                            .ToArray()
                },
            Artifacts = contents.Select(content => content.Artifact).ToArray(),
            ArtifactContents = contents
        };

    private static VeteransReviewerArtifactContent Content(
        ArtifactId artifactId,
        ArtifactId sourceId,
        int startPage,
        int endPage,
        string? printableText = null) =>
        new()
        {
            Artifact =
                new Artifact
                {
                    Id = artifactId,
                    Name = "Sleep Medicine Specialist Initial Consultation Note",
                    ArtifactType = "veterans-clinical-note",
                    Metadata =
                        new Dictionary<string, object>
                        {
                            [VeteransArtifactMetadataKeys.SourceStartPage] =
                                startPage.ToString(),
                            [VeteransArtifactMetadataKeys.SourceEndPage] =
                                endPage.ToString(),
                            [VeteransArtifactMetadataKeys.EvidenceDate] =
                                "2024-01-25",
                            [VeteransArtifactMetadataKeys.EvidenceTitle] =
                                "Sleep Medicine Specialist Initial Consultation Note"
                        }
                },
            Text = printableText ?? "Clinical note text.",
            PrintablePages =
                printableText is null
                    ? []
                    :
                    [
                        new PrintableArtifactPage
                        {
                            PageNumber = 1,
                            ContentType = "text/plain",
                            Content = Encoding.UTF8.GetBytes(printableText)
                        }
                    ],
            SourceName = "VA Blue Button Report",
            Relationships =
            [
                new Relationship
                {
                    SourceArtifactId = artifactId,
                    TargetArtifactId = sourceId,
                    RelationshipType = RelationshipTypes.DerivedFrom
                }
            ]
        };

    private sealed class SourceClarificationRepositoryStub(
        params SourceClarification[] clarifications) :
        ISourceClarificationRepository
    {
        public Task AddAsync(
            SourceClarification clarification,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SourceClarification>> GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceClarification>>(
                clarifications);
    }
}
