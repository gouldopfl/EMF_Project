using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerClinicalLayoutTests;
using static EMF.Tests.VeteransReviewerPackageSnapshotTests;

namespace EMF.Tests;

// Architectural acceptance checks: docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md.
// Add semantic cases here rather than condition- or page-specific renderer fixes.
[Trait("Category", "ReviewerDeterminism")]
public sealed class VeteransReviewerPackageDeterminismTests
{
    private static readonly VeteransReviewerPackageRenderSettings Settings = new(new(2026, 9, 29));
    private static readonly string[] Appendices =
    [
        VeteransReviewerPackageAppendix.MedicalEvidence,
        VeteransReviewerPackageAppendix.MedicalOpinionEvidence,
        VeteransReviewerPackageAppendix.ServiceRecords,
        VeteransReviewerPackageAppendix.LayEvidence,
        VeteransReviewerPackageAppendix.AdjudicativeRecords,
        VeteransReviewerPackageAppendix.MedicalLiterature
    ];
    private static readonly string[] AppendixTitles =
    [
        "Appendix A — Medical Evidence", "Appendix B — Medical Opinion Evidence",
        "Appendix C — Service Records", "Appendix D — Lay Evidence",
        "Appendix E — Adjudicative Records", "Appendix F — Medical / Scientific Literature"
    ];

    public static IEnumerable<object[]> Cases()
    {
        foreach (var condition in new[] { "Synthetic joint condition", "Synthetic respiratory condition", "Synthetic digestive condition" })
        foreach (var secondary in new[] { false, true })
        foreach (var questionnaire in new[] { "Functional activity rating scale", "Daily activity questionnaire" })
            yield return [condition, secondary, questionnaire];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FrozenInputsRepeatEveryDocumentPartAndRetainAvailableSections(
        string condition, bool secondary, string questionnaire)
    {
        var snapshot = Fixture(condition, secondary, questionnaire, secondary ? 45 : 3, allAppendices: true);
        var payload = snapshot.Payload;
        var first = VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings);
        var second = VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings);
        Assert.Equal(DocumentPresentationParts(first), DocumentPresentationParts(second));
        Assert.Equal(payload, snapshot.Payload);
        using var document = Open(first);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var body = document.MainDocumentPart!.Document!.Body!;
        var text = body.InnerText;
        foreach (var title in AppendixTitles) Assert.Contains(title, text);
        foreach (var title in new[]
        {
            "Reviewer Instructions", "Applicable VA Regulation", "Executive Summary", "Relevant Medications for Medical Opinion",
            "Key Evidence and Chronology", "Medical / Scientific Literature Considered", "Questions for the Reviewing Physician"
        }) Assert.Contains(title, text);
        Assert.Contains("Example Veteran", text);
        Assert.Contains("Example Preparer", text);
        Assert.Contains("Synthetic medication for " + condition, text);
        for (var index = 0; index < Appendices.Length; index++) Assert.Contains("SOURCE_SENTINEL_" + index, text);
        Assert.Contains("Activity 003", text);
        Assert.Contains("Synthetic regulatory text", text);
        Assert.Contains("Synthetic literature for " + condition, text);
    }

    [Fact]
    public void EquivalentSectionsUseIdenticalSharedStylesAcrossTheClaimMatrix()
    {
        string? expectedStyles = null;
        string[]? expectedHeadingStyles = null;
        string? expectedQuestionnaire = null;
        foreach (var inputs in Cases())
        {
            var snapshot = Fixture((string)inputs[0], (bool)inputs[1], (string)inputs[2], 3, allAppendices: true);
            using var document = Open(VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings));
            var part = document.MainDocumentPart!;
            var styles = part.StyleDefinitionsPart!.Styles!.OuterXml;
            var headingStyles = part.Document!.Body!.Elements<Paragraph>()
                .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1")
                .Select(p => p.InnerText + "|" + p.ParagraphProperties!.OuterXml).ToArray();
            var tables = part.Document!.Body!.Elements<Table>().Where(t => t.InnerText.Contains("Activity 001")).ToArray();
            Assert.NotEmpty(tables);
            expectedStyles ??= styles;
            expectedHeadingStyles ??= headingStyles;
            expectedQuestionnaire ??= tables[0].OuterXml;
            Assert.Equal(expectedStyles, styles);
            Assert.Equal(expectedHeadingStyles, headingStyles);
            Assert.All(tables, table => Assert.Equal(expectedQuestionnaire, table.OuterXml));
        }
    }

    [ReviewerLibreOfficeTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RepeatedPdfRetainsPageGeometrySectionPlacementAndSourceContent(bool secondary, bool allAppendices)
    {
        var snapshot = Fixture("Synthetic respiratory condition", secondary,
            secondary ? "Daily activity questionnaire" : "Functional activity rating scale",
            secondary ? 45 : 3, allAppendices);
        var converter = new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter();
        using var first = UglyToad.PdfPig.PdfDocument.Open(await converter.ConvertDocxToPdfAsync(
            VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings)));
        using var second = UglyToad.PdfPig.PdfDocument.Open(await converter.ConvertDocxToPdfAsync(
            VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings)));
        Assert.Equal(first.NumberOfPages, second.NumberOfPages);
        for (var number = 1; number <= first.NumberOfPages; number++)
        {
            var a = first.GetPage(number);
            var b = second.GetPage(number);
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.Height, b.Height);
            var aw = a.GetWords().ToArray();
            var bw = b.GetWords().ToArray();
            Assert.Equal(aw.Length, bw.Length);
            for (var index = 0; index < aw.Length; index++)
            {
                Assert.Equal(aw[index].Text, bw[index].Text);
                Assert.InRange(Math.Abs(aw[index].BoundingBox.Left - bw[index].BoundingBox.Left), 0, .1);
                Assert.InRange(Math.Abs(aw[index].BoundingBox.Bottom - bw[index].BoundingBox.Bottom), 0, .1);
                Assert.InRange(Math.Abs(aw[index].BoundingBox.Right - bw[index].BoundingBox.Right), 0, .1);
                Assert.InRange(Math.Abs(aw[index].BoundingBox.Top - bw[index].BoundingBox.Top), 0, .1);
                Assert.InRange(aw[index].BoundingBox.Left, -.5, a.Width + .5);
                Assert.InRange(aw[index].BoundingBox.Right, -.5, a.Width + .5);
                Assert.InRange(aw[index].BoundingBox.Bottom, -.5, a.Height + .5);
                Assert.InRange(aw[index].BoundingBox.Top, -.5, a.Height + .5);
            }
            // Strip running furniture so an otherwise empty page cannot pass.
            var content = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", aw.Select(w => w.Text)),
                @"CONFIDENTIAL\s*[—–-]\s*VETERAN MEDICAL INFORMATION\s*\|?|Veterans Evidence Package for Medical Review|Page \d+ of \d+", "");
            Assert.False(string.IsNullOrWhiteSpace(content), "Unexpected blank page " + number);
        }
        var pdfText = string.Join(" ", first.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
        for (var index = 0; index < (allAppendices ? 6 : 2); index++) Assert.Contains("SOURCE_SENTINEL_" + index, pdfText);
        Assert.Contains("Activity 003", pdfText);
    }

    [Fact]
    public void ExplicitDateDifferenceIsAChangedRenderInputAndAuditTimestampsAreNot()
    {
        var snapshot = Fixture("Synthetic joint condition", false, "Functional activity rating scale", 3, false);
        var early = VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings);
        var refreshedSettings = new VeteransReviewerPackageRenderSettings(new(2035, 1, 1));
        var late = VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, refreshedSettings);
        using var earlyDoc = Open(early);
        using var lateDoc = Open(late);
        Assert.Contains("after the review date (2026-09-29)", earlyDoc.MainDocumentPart!.Document!.InnerText);
        Assert.DoesNotContain("after the review date", lateDoc.MainDocumentPart!.Document!.InnerText);
        var originalAudit = ReviewerPackageOutputProvenance.Create(snapshot.PackageId, ReviewerPackageOutputFormats.Docx,
            snapshot.Sha256, VeteransReviewerPackageRendererIdentity.Contract, VeteransReviewerPackageRendererIdentity.Build,
            null, null, Settings.PackagePreparedDate, early, DateTimeOffset.Parse("2026-09-29T00:00:00Z", CultureInfo.InvariantCulture));
        var laterAudit = originalAudit with { GeneratedUtc = originalAudit.GeneratedUtc.AddDays(100) };
        laterAudit.ValidateIntegrity();
        Assert.Equal(originalAudit.ProvenanceId, laterAudit.ProvenanceId);
        Assert.Equal(DocumentPresentationParts(early), DocumentPresentationParts(
            VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings)));
        // Version creation is a host responsibility; service tests reject changing
        // this date on the preserved package and accept a distinct package version.
        Assert.NotEqual(Settings, refreshedSettings);
    }

    [Fact]
    public void DeterministicBoundaryRejectsMissingSettingsAndCorruptSnapshots()
    {
        var snapshot = Fixture("Synthetic digestive condition", true, "Daily activity questionnaire", 3, false);
        Assert.Throws<ArgumentNullException>(() => VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, null!));
        Assert.Throws<ArgumentException>(() => VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, new(default)));
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackageDocxRenderer.RenderSnapshot(
            snapshot with { Sha256 = new string('0', 64) }, Settings));
    }

    [Fact]
    public void FrozenSnapshotRenderingDoesNotDependOnAmbientCultureOrMutablePreparationData()
    {
        var metadata = new Dictionary<string, object> { ["evidenceTitle"] = "Synthetic culture title" };
        var preparation = Details("Synthetic culture fixture", metadata: metadata);
        var snapshot = VeteransReviewerPackageSnapshot.Capture(preparation, Regulations());
        snapshot = VeteransReviewerPackageOutputReuseTests.Change(snapshot, root =>
        {
            var content = root["Details"]!["ArtifactContents"]![0]!;
            content["Appendix"] = VeteransReviewerPackageAppendix.MedicalEvidence;
            content["SourceName"] = "VA Blue Button Report";
            const string text = "Factual source Synthetic culture fixture\nCompleted: 2030-01-02.";
            content["Text"] = text;
            content["PrintablePages"]![0]!["Content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        });
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var expected = DocumentPresentationParts(VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings));
            metadata["evidenceTitle"] = "Changed live preparation title";
            foreach (var name in new[] { "fr-FR", "tr-TR", "th-TH" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                var actual = DocumentPresentationParts(VeteransReviewerPackageDocxRenderer.RenderSnapshot(snapshot, Settings));
                Assert.Equal(expected.Select(p => p.Key), actual.Select(p => p.Key));
                for (var index = 0; index < expected.Length; index++)
                    Assert.Equal(expected[index].Value, actual[index].Value);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);

    internal static ReviewerPackageSnapshot Fixture(string condition, bool secondary, string questionnaire,
        int paragraphs, bool allAppendices)
    {
        var original = VeteransReviewerPackageSnapshot.Capture(Details(condition), Regulations());
        return VeteransReviewerPackageOutputReuseTests.Change(original, root =>
        {
            var details = root["Details"]!;
            // The general snapshot fixture contains a source-correction example;
            // this matrix uses different source text and no correction operation.
            details["SourceClarifications"] = new JsonArray();
            details["VeteranDisplayName"] = "Example Veteran";
            details["PackagePreparedBy"] = "Example Preparer";
            details["PackageDetails"]!["Package"]!["ServiceConnectionBasisId"] = secondary ? "synthetic-basis" : null;
            details["MedicalOpinionRequested"]!["OpinionText"] = secondary
                ? "Determine whether the Veteran's " + condition + " is at least as likely as not (50 percent or greater probability) " +
                  "proximately due to or the result of the Veteran's service-connected Synthetic basis condition. If causation " +
                  "is not established, assess aggravation and provide a supporting rationale."
                : "Review the evidence for direct service connection for " + condition + ".";
            var medication = "Synthetic medication for " + condition;
            details["CurrentMedications"]![0]!["MedicationName"] = medication;
            details["MedicationProgressions"]![0]!["MedicationName"] = medication;
            details["MedicationProgressions"]![0]!["Entries"]![0]!["MedicationName"] = medication;
            details["MedicationProgressions"]![0]!["IndicationReconciliation"]!["MedicationName"] = medication;
            details["MedicationClinicalContexts"]![0]!["MedicationName"] = medication;
            var artifact = details["Artifacts"]![0]!.DeepClone();
            var content = details["ArtifactContents"]![0]!.DeepClone();
            var membership = details["PackageDetails"]!["Artifacts"]![0]!.DeepClone();
            var artifacts = new JsonArray();
            var contents = new JsonArray();
            var members = new JsonArray();
            for (var index = 0; index < (allAppendices ? 6 : 2); index++)
            {
                var id = index == 0 ? "source" : "synthetic-source-" + index;
                var a = artifact.DeepClone();
                a["Id"] = id;
                a["Name"] = "Synthetic source " + index;
                a["Metadata"]!["evidenceTitle"] = "Synthetic source " + index;
                var c = content.DeepClone();
                c["Artifact"] = a.DeepClone();
                c["Appendix"] = allAppendices ? Appendices[index] : index == 0 ? Appendices[0] : Appendices[3];
                c["SourceName"] = "VA Blue Button Report";
                var narrative = "SOURCE_SENTINEL_" + index + "\n" +
                    (allAppendices && index == 5 ? "Synthetic literature for " + condition + "\n" : "") +
                    "Completed: 2030-01-02.\n\n" + questionnaire +
                    ": documented scores\nActivity 001 3/10\nActivity 002 4/10\nActivity 003 5/10\nSOCIAL: preserved source text.\n\n" +
                    string.Join("\n\n", Enumerable.Range(1, paragraphs).Select(n =>
                        "Synthetic factual evidence paragraph " + n + ". Original source wording and sequence remain available for review."));
                c["Text"] = narrative;
                c["PrintablePages"]![0]!["Content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(narrative));
                c["MedicalLiteratureReviewerText"] = "SOURCE_SENTINEL_" + index + " Synthetic literature for " + condition;
                foreach (var classification in c["ReviewedMedicalLiteratureClassifications"]!.AsArray()) classification!["ArtifactId"] = id;
                var m = membership.DeepClone();
                m["ArtifactId"] = id;
                artifacts.Add(a); contents.Add(c); members.Add(m);
            }
            details["Artifacts"] = artifacts;
            details["ArtifactContents"] = contents;
            details["PackageDetails"]!["Artifacts"] = members;
        });
    }
}

public sealed class ReviewerLibreOfficeTheoryAttribute : TheoryAttribute
{
    public ReviewerLibreOfficeTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("EMF_REVIEWER_LAYOUT_TESTS") != "true")
            Skip = "Set EMF_REVIEWER_LAYOUT_TESTS=true to execute the required determinism PDF checks with LibreOffice.";
    }
}
