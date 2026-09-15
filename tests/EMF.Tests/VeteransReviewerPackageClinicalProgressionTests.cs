using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageClinicalProgressionTests
{
    [Fact]
    public async Task GetAsync_ProjectsDirectPackageEvidenceWithoutInternalIdentity()
    {
        var issueId = new ClaimIssueId("issue-osa");
        var artifactId = new ArtifactId("sleep-note-internal");
        var details = Details(issueId, Content(artifactId));
        var repository =
            new ClinicalProgressionRepositoryStub(
                Event(
                    issueId,
                    artifactId,
                    new DateOnly(2025, 10, 27),
                    ClinicalProgressionEventTypes.TreatmentResponse,
                    "AHI improved on ASV."));

        var projected =
            await new VeteransReviewerPackageClinicalProgressionService(repository)
                .GetAsync(details);

        var item = Assert.Single(projected);

        Assert.Equal(artifactId, item.ReviewerArtifactId);
        Assert.Equal(new DateOnly(2025, 10, 27), item.EventDate);
        Assert.Equal(ClinicalProgressionEventTypes.TreatmentResponse, item.EventType);
        Assert.Equal(
            "VA Blue Button Report — Sleep Med Remote PAP Follow-Up Note — October 27, 2025",
            item.SourceLocator);
        Assert.Equal("AHI improved on ASV.", item.Summary);

        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("sleep-note-internal", json);
        Assert.DoesNotContain("clinical-note-2025-10-27", json);
        Assert.DoesNotContain("803", json);
    }

    [Fact]
    public async Task GetAsync_MapsPagedParentSourceToContainingBoundedRecordOnly()
    {
        var issueId = new ClaimIssueId("issue-osa");
        var sourceId = new ArtifactId("blue-button-internal");
        var affectedId = new ArtifactId("affected-note");
        var unrelatedId = new ArtifactId("unrelated-note");
        var details =
            Details(
                issueId,
                Content(affectedId, sourceId, 1140, 1142),
                Content(unrelatedId, sourceId, 1213, 1215));

        var repository =
            new ClinicalProgressionRepositoryStub(
                Event(
                    issueId,
                    sourceId,
                    new DateOnly(2025, 7, 8),
                    ClinicalProgressionEventTypes.TreatmentProblem,
                    "Residual AHI remained elevated.",
                    1140,
                    1142));

        var projected =
            await new VeteransReviewerPackageClinicalProgressionService(repository)
                .GetAsync(details);

        var item = Assert.Single(projected);
        Assert.Equal(affectedId, item.ReviewerArtifactId);
        Assert.DoesNotContain("1140", item.SourceLocator);
        Assert.DoesNotContain("blue-button-internal", item.SourceLocator);
    }

    [Fact]
    public async Task GetAsync_OmitsEventWhoseSourceIsNotInReviewerPackage()
    {
        var issueId = new ClaimIssueId("issue-osa");
        var details = Details(issueId, Content(new ArtifactId("included-note")));
        var repository =
            new ClinicalProgressionRepositoryStub(
                Event(
                    issueId,
                    new ArtifactId("outside-package"),
                    new DateOnly(2025, 7, 30),
                    ClinicalProgressionEventTypes.TreatmentTransition,
                    "Transitioned to ASV."));

        var projected =
            await new VeteransReviewerPackageClinicalProgressionService(repository)
                .GetAsync(details);

        Assert.Empty(projected);
    }

    [Fact]
    public void Render_ShowsClinicalProgressionWithoutInternalProvenance()
    {
        var issueId = new ClaimIssueId("issue-osa");
        var artifactId = new ArtifactId("sleep-note-internal");
        var details = Details(issueId, Content(artifactId));

        details =
            new VeteransReviewerPackageDetails
            {
                PackageDetails = details.PackageDetails,
                Artifacts = details.Artifacts,
                ArtifactContents = details.ArtifactContents,
                ClinicalProgressionEvents =
                [
                    new VeteransReviewerClinicalProgressionEvent
                    {
                        ReviewerArtifactId = artifactId,
                        EventDate = new DateOnly(2025, 10, 27),
                        EventType = ClinicalProgressionEventTypes.TreatmentResponse,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Med Remote PAP Follow-Up Note — October 27, 2025",
                        Summary =
                            "On AirCurve 11 ASV, PAP compliance was 100% and residual AHI was 5.8 per hour."
                    }
                ]
            };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("Clinical Progression", text);
        Assert.Contains("October 27, 2025 — Treatment Response", text);
        Assert.Contains("PAP compliance was 100%", text);
        Assert.Contains(
            "Source: VA Blue Button Report — Sleep Med Remote PAP Follow-Up Note — October 27, 2025",
            text);
        Assert.DoesNotContain("sleep-note-internal", text);
        Assert.DoesNotContain("clinical-note-2025-10-27", text);
        Assert.DoesNotContain("Source pages: 803", text);
    }

    private static ClinicalProgressionEvent Event(
        ClaimIssueId issueId,
        ArtifactId sourceArtifactId,
        DateOnly eventDate,
        string eventType,
        string summary,
        int? startPage = null,
        int? endPage = null) =>
        new()
        {
            Id = new ClinicalProgressionEventId("event-1"),
            ClaimIssueId = issueId,
            SourceArtifactId = sourceArtifactId,
            EventDate = eventDate,
            SourceStartPage = startPage,
            SourceEndPage = endPage,
            RecordTitle =
                eventDate == new DateOnly(2025, 10, 27)
                    ? "Sleep Med Remote PAP Follow-Up Note"
                    : "Sleep Med PAP Record",
            EventType = eventType,
            Summary = summary
        };

    private static VeteransReviewerPackageDetails Details(
        ClaimIssueId issueId,
        params VeteransReviewerArtifactContent[] contents)
    {
        var packageId = new EvidencePackageId("package-1");

        return new VeteransReviewerPackageDetails
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
    }

    private static VeteransReviewerArtifactContent Content(
        ArtifactId artifactId,
        ArtifactId? parentSourceId = null,
        int startPage = 803,
        int endPage = 804) =>
        new()
        {
            Artifact =
                new Artifact
                {
                    Id = artifactId,
                    Name = "clinical-note-2025-10-27-803-804.txt",
                    ArtifactType = "veterans-clinical-note",
                    Metadata =
                        new Dictionary<string, object>
                        {
                            [VeteransArtifactMetadataKeys.SourceStartPage] =
                                startPage.ToString(),
                            [VeteransArtifactMetadataKeys.SourceEndPage] =
                                endPage.ToString(),
                            [VeteransArtifactMetadataKeys.EvidenceDate] =
                                "2025-10-27",
                            [VeteransArtifactMetadataKeys.EvidenceTitle] =
                                "Sleep Med Remote PAP Follow-Up Note"
                        }
                },
            Text = "Clinical note text.",
            PrintablePages =
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "text/plain",
                    Content = Encoding.UTF8.GetBytes("Clinical note text.")
                }
            ],
            Appendix = VeteransReviewerPackageAppendix.MedicalEvidence,
            SourceName = "VA Blue Button Report",
            Relationships =
                parentSourceId is null
                    ? []
                    :
                    [
                        new Relationship
                        {
                            SourceArtifactId = artifactId,
                            TargetArtifactId = parentSourceId.Value,
                            RelationshipType = RelationshipTypes.DerivedFrom
                        }
                    ]
        };

    private sealed class ClinicalProgressionRepositoryStub(
        params ClinicalProgressionEvent[] events) :
        IClinicalProgressionRepository
    {
        public Task AddAsync(
            ClinicalProgressionEvent progressionEvent,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClinicalProgressionEvent>> GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClinicalProgressionEvent>>(events);
    }
}
