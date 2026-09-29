using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerNativeEvidencePageTests;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

public sealed class VeteransReviewerPatientHeaderTests
{
    private const string Identity = "Example-Surname, Robin Allen Date of birth: December 3, 1971";

    private static PrintableArtifactPage[] Pages(string before, string after, bool openingHeader = false, bool splitDate = false,
        bool pageEdgeBlock = false, double beforeX = 45, bool painContext = false)
    {
        using var first = new NativePage(1224, 1584);
        if (openingHeader) first.Line(Identity, 20, font: "SourceSansPro");
        first.Line("CCC: CLINICAL TRIAGE", 50, font: "SourceSansPro");
        first.Line("The patient reports persistent symptoms after prolonged standing", 100, x: 45);
        first.Line("and walking despite treatment with activity modification.", 115, x: 45);
        first.Line("The clinician reviewed the home exercise program and", 145, x: 45);
        first.Line("recommended continuation with regular follow-up visits.", 160, x: 45);
        first.Line("The patient understands the proposed treatment plan and", 190, x: 45);
        first.Line("will return for further assessment after physical therapy.", 205, x: 45);
        if (painContext && before == "number from 0-10: 6")
            first.Line("During the past 24 hours, how much has pain affected your mood?", 732, x: 45);
        first.Line(before, pageEdgeBlock ? 750 : 235, x: beforeX);
        using var second = new NativePage(1224, 1584);
        second.Line(splitDate ? Identity[..^4].TrimEnd() : Identity, 20, font: "SourceSansPro");
        if (splitDate) second.Line("1971", 35, font: "SourceSansPro");
        second.Line(after, 80, x: 45);
        second.Line("Assessment: Continued symptoms require follow-up care.", 110, x: 45);
        using var third = new NativePage(1224, 1584);
        third.Line(Identity.Replace(" Date", "     Date"), 20, font: "SourceSansPro");
        third.Line("Plan: Continue the prescribed home exercise program.", 80, x: 45);
        return [first.Page(pageNumber: 10), second.Page(pageNumber: 11), third.Page(pageNumber: 12)];
    }

    [Theory]
    [InlineData("Foot and heel pain is increased w/ prolonged", "standing, walking and driving.", false, false)]
    [InlineData("He has been", "having low back pain that radiates down the left leg.", false, false)]
    [InlineData("Foot and heel pain is increased w/ prolonged", "standing, walking and driving.", true, false)]
    [InlineData("He has been", "having low back pain that radiates down the left leg.", false, true)]
    public void RunningIdentityMovesToOpeningMetadataAndInterruptedWordsRejoin(
        string before, string after, bool openingHeader, bool splitDate)
    {
        var pages = Pages(before, after, openingHeader, splitDate);
        var originalBytes = pages.Select(p => p.Content.ToArray()).ToArray();
        var originalGlyphs = pages.Select(p => p.TextGeometry!.Glyphs.ToArray()).ToArray();
        var paragraphs = Assert.IsAssignableFrom<IReadOnlyList<string>>(VeteransReviewerNativeProse.Reconstruct(pages));
        Assert.Equal(Identity, paragraphs[0]);
        Assert.Single(paragraphs.Where(p => p.Contains("Date of birth:")));
        Assert.Contains(before + " " + after, paragraphs);
        // Every non-header word remains in source order, including punctuation.
        var raw = string.Join(" ", pages.SelectMany(VeteransReviewerNativeProse.Lines).Select(l => l.Text));
        raw = Regex.Replace(raw, @"Example-Surname, Robin Allen\s+Date of birth: December 3, 1971", "");
        Assert.Equal(raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            string.Join(" ", paragraphs.Skip(1)).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        for (var i = 0; i < pages.Length; i++)
        {
            Assert.Equal(originalBytes[i], pages[i].Content.ToArray());
            Assert.Equal(originalGlyphs[i], pages[i].TextGeometry!.Glyphs);
        }
    }

    [Theory]
    [InlineData("Foot and heel pain is increased w/ prolonged", "standing, walking and driving.")]
    [InlineData("He has been", "having low back pain that radiates down the left leg.")]
    public void FirstRetainedHeaderInMidRecordExcerptIsMovedEvenWithoutEarlierIdentity(string before, string after)
    {
        var pages = Pages(before, after)[..2];
        var paragraphs = VeteransReviewerNativeProse.Reconstruct(pages)!;
        Assert.Equal(Identity, paragraphs[0]);
        Assert.Single(paragraphs.Where(p => p.Contains("Date of birth:")));
        Assert.Contains(before + " " + after, paragraphs);
    }

    [Theory]
    [InlineData("From: Example, Robin", "To: Clinical Team", 45)]
    [InlineData("\"I feel a little better today leaving than when I came in.\"", "O:", 45)]
    [InlineData("number from 0-10: 6", "During the past 24 hours, how much has pain contributed to your stress?", 59.4)]
    public void FirstRetainedRunningIdentityMovesBeforeStructuredPageEdgeBlock(string before, string after, double beforeX)
    {
        var pages = Pages(before, after, pageEdgeBlock: true, beforeX: beforeX)[..2];
        var originalBytes = pages.Select(p => p.Content.ToArray()).ToArray();
        var originalGlyphs = pages.Select(p => p.TextGeometry!.Glyphs.ToArray()).ToArray();
        var paragraphs = VeteransReviewerNativeProse.Reconstruct(pages)!;
        Assert.Equal(Identity, paragraphs[0]);
        Assert.Single(paragraphs.Where(p => p.Contains("Date of birth:")));
        Assert.Contains(before, paragraphs);
        Assert.Contains(after, paragraphs);
        var sourceWords = string.Join(" ", pages.SelectMany(VeteransReviewerNativeProse.Lines)
            .Where(line => line.Text != Identity).Select(line => line.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(sourceWords, string.Join(" ", paragraphs.Skip(1)).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        for (var i = 0; i < pages.Length; i++)
        {
            Assert.Equal(originalBytes[i], pages[i].Content.ToArray());
            Assert.Equal(originalGlyphs[i], pages[i].TextGeometry!.Glyphs);
        }
    }

    [Fact]
    public void SingleIdentityWithoutPrecedingPageEdgeContentStaysInPlace()
    {
        var paragraphs = VeteransReviewerNativeProse.Reconstruct(Pages("From: Example, Robin", "To: Clinical Team")[..2])!;
        Assert.NotEqual(Identity, paragraphs[0]);
        Assert.True(paragraphs.ToList().IndexOf("From: Example, Robin") < paragraphs.ToList().IndexOf(Identity));
    }

    [ReviewerLibreOfficeFact]
    public async Task StructuredPageEdgeBlocksKeepTheirWordsAndOpeningIdentityInDocxAndPdf()
    {
        foreach (var (before, after, beforeX) in new[] {
            ("From: Example, Robin", "To: Clinical Team", 45d),
            ("\"I feel a little better today leaving than when I came in.\"", "O:", 45d),
            ("number from 0-10: 6", "During the past 24 hours, how much has pain contributed to your stress?", 59.4) })
        {
            var content = new VeteransReviewerArtifactContent
            {
                Artifact = new() { Id = new("structured-header"), Name = "Clinical Triage", ArtifactType = "veterans-clinical-note" },
                Appendix = VeteransReviewerPackageAppendix.MedicalEvidence, SourceName = "VA Blue Button Report",
                Text = "Original stored evidence remains untouched.",
                PrintablePages = Pages(before, after, pageEdgeBlock: true, beforeX: beforeX, painContext: true)[..2]
            };
            var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([content]));
            using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
            Assert.Empty(doc.MainDocumentPart!.ImageParts);
            var text = doc.MainDocumentPart!.Document!.InnerText;
            Assert.Single(Regex.Matches(text, "Example-Surname,"));
            // Structured fields can render as separate table cells; InnerText
            // does not insert spaces between those cells.
            var compact = Regex.Replace(text, @"\s+", "");
            var compactBefore = Regex.Replace(before, @"\s+", "");
            var compactAfter = Regex.Replace(after, @"\s+", "");
            Assert.Contains(compactBefore, compact);
            Assert.Contains(compactAfter, compact);
            Assert.True(compact.IndexOf("Example-Surname,", StringComparison.Ordinal) < compact.IndexOf(compactBefore, StringComparison.Ordinal));
            Assert.True(compact.IndexOf(compactBefore, StringComparison.Ordinal) < compact.IndexOf(compactAfter, StringComparison.Ordinal));
            var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
            var visualWords = new List<string>();
            foreach (var page in pdf.GetPages())
            {
                var remaining = page.GetWords().OrderByDescending(w => w.Letters[0].StartBaseLine.Y).ToList();
                while (remaining.Count > 0)
                {
                    var baseline = remaining[0].Letters[0].StartBaseLine.Y;
                    var line = remaining.TakeWhile(w => baseline - w.Letters[0].StartBaseLine.Y < 1).ToArray();
                    visualWords.AddRange(line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
                    remaining.RemoveRange(0, line.Length);
                }
            }
            var words = string.Join(" ", visualWords);
            Assert.Single(Regex.Matches(words, "Example-Surname,"));
            var compactPdf = Regex.Replace(words, @"\s+", "");
            Assert.Contains(compactBefore + compactAfter, compactPdf);
            Assert.True(compactPdf.IndexOf("Example-Surname,", StringComparison.Ordinal) < compactPdf.IndexOf(compactBefore, StringComparison.Ordinal));
            Assert.Equal("Original stored evidence remains untouched.", content.Text);
        }
    }

    [ReviewerLibreOfficeFact]
    public async Task PowerFormDemographicFieldRemainsInDocxAndPdf()
    {
        const string demographic = "Sex/DOB/Age: Male December 3, 1971 54 years";
        const string source = "PowerForm Textual Rendition Notes\n" + demographic + "\nAssessment: Continued physical therapy.";
        Assert.Contains(demographic, VeteransReviewerPowerForm.Lines(source));
        var content = new VeteransReviewerArtifactContent
        {
            Artifact = new() { Id = new("form-control"), Name = "Physical Therapy Form", ArtifactType = "file" },
            Appendix = VeteransReviewerPackageAppendix.MedicalEvidence, Text = source
        };
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([content]));
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Contains(demographic, doc.MainDocumentPart!.Document!.InnerText);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        Assert.Contains(demographic, string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)));
        Assert.Equal(source, content.Text);
    }

    [Theory]
    [InlineData("García, Renée Date of birth: February 9, 1982", true)]
    [InlineData("O’Neil, Sam Date of birth: 09/12/1968", true)]
    [InlineData("Patient name: Robin Example Date of birth: 1971-12-03", true)]
    [InlineData("Date of birth: December 3, 1971", false)]
    [InlineData("The patient reports Date of birth: December 3, 1971", false)]
    [InlineData("Example, Robin Date of birth: December 3, 1971; symptoms started later", false)]
    public void IdentityRecognitionRequiresCompleteNamedHeader(string text, bool expected) =>
        Assert.Equal(expected, VeteransReviewerPatientHeader.IsIdentity(text));

    [Fact]
    public void BodyIdentityAndClinicalDobMentionsAreNotRunningHeaders()
    {
        var pages = Pages("He has been", "having low back pain.");
        using var body = new NativePage(1224, 1584);
        body.Line("Clinical history describes the circumstances of this visit.", 150, x: 45);
        body.Line(Identity, 200, font: "SourceSansPro");
        body.Line("Date of birth documented in clinical history: 1971-12-03", 230, x: 45);
        var paragraphs = VeteransReviewerNativeProse.Reconstruct([.. pages, body.Page(pageNumber: 13)])!;
        Assert.Equal(2, paragraphs.Count(p => p == Identity));
        Assert.Contains(paragraphs, p => p.Contains("Date of birth documented in clinical history:"));
    }

    [ReviewerLibreOfficeFact]
    public async Task BothInterruptedPatternsRemainContiguousInDocxAndPdfWithIdentityPreserved()
    {
        foreach (var (before, after) in new[] {
            ("Foot and heel pain is increased w/ prolonged", "standing, walking and driving."),
            ("He has been", "having low back pain that radiates down the left leg.") })
        {
            var pages = Pages(before, after);
            var content = new VeteransReviewerArtifactContent
            {
                Artifact = new() { Id = new("header-test"), Name = "Clinical Triage", ArtifactType = "veterans-clinical-note" },
                Appendix = VeteransReviewerPackageAppendix.MedicalEvidence, SourceName = "VA Blue Button Report",
                Text = "Original stored evidence remains untouched.", PrintablePages = pages
            };
            var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([content]));
            using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
            var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToArray();
            Assert.Empty(doc.MainDocumentPart.ImageParts);
            var narrative = Assert.Single(paragraphs.Where(p => p.InnerText.Contains(before + " " + after)));
            Assert.DoesNotContain("Date of birth:", narrative.InnerText);
            var identity = Assert.Single(paragraphs.Where(p => p.InnerText.Contains("Date of birth:")));
            Assert.True(Array.IndexOf(paragraphs, identity) < Array.IndexOf(paragraphs, narrative));
            Assert.DoesNotContain("CCC: CLINICAL TRIAGE", doc.MainDocumentPart.Document.InnerText);
            var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
            var words = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
            Assert.Contains(before + " " + after, words);
            Assert.Single(Regex.Matches(words, "Example-Surname,").Cast<Match>());
            Assert.Contains("December 3, 1971", words);
            Assert.Equal("Original stored evidence remains untouched.", content.Text);
        }
    }
}
