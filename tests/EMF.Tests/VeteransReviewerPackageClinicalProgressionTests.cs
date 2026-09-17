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

    [Fact]
    public void Render_PresentsPapAdherenceAndTitrationContextBeforeClinicalProgression()
    {
        var issueId = new ClaimIssueId("issue-osa");
        var noteArtifactId = new ArtifactId("sleep-note-internal");
        var oscarArtifactId = new ArtifactId("oscar-internal");
        var titrationArtifactId = new ArtifactId("titration-2013-internal");
        var noteContent = Content(noteArtifactId);
        var oscarText =
            """
PAP Therapy Analysis

Coverage: 07/30/2025 through 09/07/2026
Sessions: 1162
Treatment days: 404
Total therapy hours: 3114.24
Average hours per treatment day: 7.71
Days >= 4 hours: 401
Days >= 6 hours: 367
Additional same-day sessions: 758

Weighted AHI: 3.63
Median daily AHI: 2.41
Maximum daily AHI: 21.90

Machine(s): AirCurve11ASV

Derived deterministically from the retained OSCAR session export.
Raw OSCAR session records are intentionally omitted from this physician report.
""";
        var oscarContent =
            new VeteransReviewerArtifactContent
            {
                Artifact =
                    new Artifact
                    {
                        Id = oscarArtifactId,
                        Name = "OSCAR_session_export.csv",
                        ArtifactType = "file"
                    },
                Text = oscarText,
                PrintablePages =
                [
                    new PrintableArtifactPage
                    {
                        PageNumber = 1,
                        ContentType = "text/plain",
                        Content = Encoding.UTF8.GetBytes(oscarText)
                    }
                ],
                Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
            };

        var titrationContent =
            new VeteransReviewerArtifactContent
            {
                Artifact =
                    new Artifact
                    {
                        Id = titrationArtifactId,
                        Name =
                            "Jupiter-Medical-Center-CPAP-Titration-2013-11-07-page-1-of-6.pdf",
                        ArtifactType = "file"
                    },
                Text =
                    "Jupiter Medical Center nocturnal polysomnogram with CPAP titration.",
                PrintablePages =
                [
                    new PrintableArtifactPage
                    {
                        PageNumber = 1,
                        ContentType = "text/plain",
                        Content =
                            Encoding.UTF8.GetBytes(
                                "Jupiter Medical Center nocturnal polysomnogram with CPAP titration.")
                    }
                ],
                Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
            };

        var details =
            Details(
                issueId,
                noteContent,
                titrationContent,
                oscarContent);

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
                        ReviewerArtifactId = noteArtifactId,
                        EventDate = new DateOnly(2021, 8, 16),
                        EventType = ClinicalProgressionEventTypes.TreatmentAdjustment,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Med Remote PAP Follow-Up Note — August 16, 2021",
                        Summary =
                            "PAP compliance was 95.3% with average use of 7 hours 27 minutes; residual AHI was 9.3 per hour."
                    },
                    new VeteransReviewerClinicalProgressionEvent
                    {
                        ReviewerArtifactId = noteArtifactId,
                        EventDate = new DateOnly(2024, 1, 25),
                        EventType = ClinicalProgressionEventTypes.TreatmentProblem,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Medicine Specialist Initial Consultation Note — January 25, 2024",
                        Summary =
                            "The note documents 100% PAP compliance, average use of 8 hours 35 minutes, and residual AHI 3.2 per hour."
                    },
                    new VeteransReviewerClinicalProgressionEvent
                    {
                        ReviewerArtifactId = noteArtifactId,
                        EventDate = new DateOnly(2025, 6, 3),
                        EventType = ClinicalProgressionEventTypes.DiagnosticFinding,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Med Telephone Note — June 6, 2025",
                        Summary =
                            "The June 6, 2025 VA Sleep Medicine telephone note documents results of a Community Care PAP titration performed June 3, 2025. CPAP was titrated from 9-12 cmH20, with significant improvement at CPAP 12 cmH20, AHI 0 per hour, and no hypoxemia."
                    },
                    new VeteransReviewerClinicalProgressionEvent
                    {
                        ReviewerArtifactId = noteArtifactId,
                        EventDate = new DateOnly(2025, 6, 6),
                        EventType = ClinicalProgressionEventTypes.TreatmentTransition,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Med Telephone Note — June 6, 2025",
                        Summary =
                            "After discussion of continued CPAP monitoring versus empiric ASV, the Veteran preferred transition to ASV."
                    },
                    new VeteransReviewerClinicalProgressionEvent
                    {
                        ReviewerArtifactId = noteArtifactId,
                        EventDate = new DateOnly(2025, 7, 8),
                        EventType = ClinicalProgressionEventTypes.TreatmentProblem,
                        SourceLocator =
                            "VA Blue Button Report — Sleep Med Remote PAP Follow-Up Note — July 8, 2025",
                        Summary =
                            "PAP compliance was 90.1% with average use of 7 hours 52 minutes; residual AHI was 32.7 per hour."
                    }
                ]
            };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var text = body.InnerText;

        Assert.Contains("PAP Adherence / Compliance Summary", text);
        Assert.Contains(
            "Documented clinic compliance: August 16, 2021 — 95.3%; January 25, 2024 — 100%; July 8, 2025 — 90.1%.",
            text);
        Assert.Contains(
            "OSCAR session summary (07/30/2025 through 09/07/2026): 1162 sessions across 404 treatment days, 3114.24 total therapy hours, average 7.71 hours per treatment day, 401 days with at least 4 hours of use, and 367 days with at least 6 hours of use. Therapy metrics: weighted AHI 3.63, median daily AHI 2.41, and maximum daily AHI 21.90.",
            text);
        Assert.Contains(
            "periods of elevated residual AHI during intervals that also show strong PAP adherence",
            text);
        Assert.Contains("Sleep Study / PAP Titration Results", text);
        Assert.Contains(
            "primary study material supplied in the medical-evidence appendix",
            text);
        Assert.Contains("2013 — Primary PAP Titration Study", text);
        Assert.Contains(
            "historical treatment evidence",
            text);
        Assert.Contains(
            "Source: CPAP Titration Study — Jupiter Medical Center",
            text);
        Assert.Contains("June 3, 2025 — PAP Titration Result", text);
        Assert.Contains("AHI 0 per hour, and no hypoxemia", text);
        Assert.True(
            text.IndexOf(
                "2013 — Primary PAP Titration Study",
                StringComparison.Ordinal) <
            text.IndexOf(
                "June 3, 2025 — PAP Titration Result",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "Jupiter-Medical-Center-CPAP-Titration-2013-11-07-page-1-of-6.pdf",
            text);
        Assert.Contains(
            "corroborates that these documented titration findings were incorporated into treatment decisions",
            text);

        var headingOnes =
            body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Where(
                    paragraph =>
                        paragraph.ParagraphProperties?
                            .ParagraphStyleId?
                            .Val?
                            .Value == "Heading1")
                .Select(paragraph => paragraph.InnerText)
                .ToArray();

        var adherenceIndex =
            Array.IndexOf(
                headingOnes,
                "PAP Adherence / Compliance Summary");
        var titrationIndex =
            Array.IndexOf(
                headingOnes,
                "Sleep Study / PAP Titration Results");
        var progressionIndex =
            Array.IndexOf(
                headingOnes,
                "Clinical Progression");

        Assert.True(adherenceIndex >= 0);
        Assert.True(titrationIndex > adherenceIndex);
        Assert.True(progressionIndex > titrationIndex);

        var paragraphs =
            body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        var adherenceHeadingIndex =
            Array.FindIndex(
                paragraphs,
                paragraph =>
                    paragraph.InnerText ==
                        "PAP Adherence / Compliance Summary" &&
                    paragraph.ParagraphProperties?
                        .ParagraphStyleId?
                        .Val?
                        .Value == "Heading1");

        Assert.True(adherenceHeadingIndex > 0);
        Assert.NotNull(
            paragraphs[adherenceHeadingIndex - 1]
                .ParagraphProperties?
                .PageBreakBefore);
        Assert.Null(
            paragraphs[adherenceHeadingIndex]
                .ParagraphProperties?
                .PageBreakBefore);
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
