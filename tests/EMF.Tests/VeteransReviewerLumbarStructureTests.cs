using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;
using static EMF.Tests.VeteransReviewerNativeEvidencePageTests;

namespace EMF.Tests;

public sealed class VeteransReviewerLumbarStructureTests
{
    private static VeteransReviewerArtifactContent Evidence(string id, string text = "", string? parent = null,
        IReadOnlyList<PrintableArtifactPage>? pages = null, string appendix = "MedicalEvidence",
        string type = "veterans-clinical-note") => new()
    {
        Artifact = new() { Id = new(id), Name = id, ArtifactType = type, Metadata = new Dictionary<string, object> { ["evidenceTitle"] = Path.GetFileNameWithoutExtension(id) } }, Text = text,
        Appendix = appendix, SourceName = "VA Blue Button Report", PrintablePages = pages ?? [],
        PrintableSourceArtifactId = parent is null ? null : new(parent)
    };

    private static WordprocessingDocument Render(params VeteransReviewerArtifactContent[] contents) =>
        WordprocessingDocument.Open(new MemoryStream(VeteransReviewerPackageDocxRenderer.Render(Details(contents))), false);

    [ReviewerLibreOfficeFact]
    public async Task StoredTextPtHeaderSuppliesChronologyDateAndAssociatedLayPlacement()
    {
        const string ptText = "PT Outpatient Rehab Progress Note/Re-Eval\r\nDate entered: September 25, 2026, 11:30 a.m. EDT\r\n\r\n" +
            "PowerForm Textual Rendition Notes\r\nSex/DOB/Age: Male 70 years\r\nPhysical therapy assessment and plan.";
        var pt = new VeteransReviewerArtifactContent
        {
            Artifact = new() { Id = new("stored-pt"), Name = "PT Outpatient Rehab Progress Note_09252026.txt", ArtifactType = "file" },
            Text = ptText, Appendix = "MedicalEvidence"
        };
        var lay = new VeteransReviewerArtifactContent
        {
            Artifact = new() { Id = new("stored-lay"), Name = "Veteran Lay Clarification_AFO Fit and Falls_09252026.txt", ArtifactType = "file" },
            Text = "Unchanged lay clarification body.", Appendix = "LayEvidence"
        };
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([pt, lay]));
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var headings = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2").Select(p => p.InnerText).ToArray();
        const string title = "PT Outpatient Rehab Progress Note/Re-Eval";
        Assert.Contains("2026-09-25 — " + title, headings);
        Assert.Equal("Associated Veteran Lay Clarification — AFO Fit and Falls", headings[Array.LastIndexOf(headings, title) + 1]);
        Assert.DoesNotContain("Additional Evidence", doc.MainDocumentPart.Document.InnerText);
        Assert.Equal("LayEvidence", lay.Appendix);
        Assert.Empty(pt.Artifact.Metadata);
        Assert.Equal(ptText, pt.Text);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var words = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        Assert.Contains("2026-09-25 — " + title, words);
        Assert.Contains("Associated Veteran Lay Clarification — AFO Fit and Falls", words);
        Assert.DoesNotContain("Additional Evidence", words);
    }

    [Theory]
    [InlineData("PT record\nDate entered: September 25, 2026, 11:30 a.m. EDT", "2026-09-25")]
    [InlineData("PT record\nDate entered: August 17, 2025", "2025-08-17")]
    [InlineData("PT record\nDate entered: February 31, 2026", null)]
    [InlineData("PT record\nAssessment: follow up September 25, 2026", null)]
    [InlineData("PT record\nClinical assessment\nDate entered: September 25, 2026", null)]
    public void EncounterDateFallbackUsesOnlyExplicitOpeningHeader(string text, string? expected)
    {
        Assert.Equal(expected, VeteransReviewerEncounterHeader.Read(Evidence("record.txt", text))?.Date);
        Assert.Null(VeteransReviewerEncounterHeader.Read(Evidence("lay.txt", text, appendix: "LayEvidence", type: "file")));
    }

    [ReviewerLibreOfficeFact]
    public async Task AssociatedClarificationFollowsPtAndPreservesLayClassificationInDocxAndPdf()
    {
        var pt = Evidence("PT Outpatient Rehab Progress Note/Re-Eval", "PT clinical body.");
        ((Dictionary<string, object>)pt.Artifact.Metadata)["evidenceDate"] = "2026-09-25";
        ((Dictionary<string, object>)pt.Artifact.Metadata)["evidenceTitle"] = pt.Artifact.Name;
        var lay = Evidence("Veteran Lay Clarification AFO Fit and Falls September 25, 2026",
            "Unchanged lay clarification body.", appendix: "LayEvidence", type: "file");
        var details = Details([lay, pt,
            Evidence("Spousal Statement", "Spousal body", appendix: "LayEvidence", type: "file"),
            Evidence("Veteran Personal Statement", "Veteran body", appendix: "LayEvidence", type: "file")]);
        Assert.Equal("LayEvidence", VeteransReviewerSourceMembership.Select(details, [lay]).Single().Appendix);
        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var headings = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2").Select(p => p.InnerText).ToArray();
        Assert.Equal("Associated Veteran Lay Clarification — AFO Fit and Falls", headings[Array.IndexOf(headings, pt.Artifact.Name) + 1]);
        var text = doc.MainDocumentPart.Document.InnerText;
        Assert.DoesNotContain("Additional Evidence", text);
        Assert.Equal(1, text.Split("Unchanged lay clarification body.").Length - 1);
        Assert.Equal("LayEvidence", lay.Appendix);
        Assert.Equal("Unchanged lay clarification body.", lay.Text);
        var appendixD = text[text.LastIndexOf("Appendix D — Lay Evidence", StringComparison.Ordinal)..];
        appendixD = appendixD[..appendixD.IndexOf("Appendix E —", StringComparison.Ordinal)];
        Assert.DoesNotContain("clarification body", appendixD);
        Assert.Contains("Spousal body", appendixD); Assert.Contains("Veteran body", appendixD);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var pdfText = string.Join("\n", pdf.GetPages().Select(p => p.Text));
        Assert.DoesNotContain("Additional Evidence", pdfText);
        Assert.True(pdfText.IndexOf("PT clinical body.", StringComparison.Ordinal) < pdfText.IndexOf("Unchanged lay clarification body.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PT Outpatient Rehab Progress Note_09252026.txt", true)]
    [InlineData("PT Outpatient Rehab Progress Note_08252026.txt", false)]
    public void AssociationUsesExactEncounterAndRetainsUnmatchedEvidence(string filename, bool associated)
    {
        var pt = Evidence(filename, "PT body");
        ((Dictionary<string, object>)pt.Artifact.Metadata)["evidenceTitle"] = "PT Outpatient Rehab Progress Note/Re-Eval";
        var lay = Evidence("Veteran Lay Clarification AFO Fit and Falls September 25, 2026", "Lay body", appendix: "LayEvidence", type: "file");
        using var doc = Render(pt, lay);
        var text = doc.MainDocumentPart!.Document.InnerText;
        Assert.Equal(associated, text.Contains("Associated Veteran Lay Clarification — AFO Fit and Falls"));
        Assert.Equal(!associated, text.Contains("Additional Evidence"));
        Assert.Contains("Lay body", text);
    }

    [ReviewerLibreOfficeFact]
    public async Task PatientHeaderGapAndDetachedDobRenderOnOnePdfBaseline()
    {
        const string source = "Example, Patient Allen                                                Date of birth:\nDecember 3, 1971\nClinical assessment: unchanged";
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([Evidence("Patient header", source)]));
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var header = Assert.Single(doc.MainDocumentPart!.Document!.Descendants<Paragraph>().Where(p => p.InnerText.Contains("Date of birth:")));
        Assert.Equal("Example, Patient Allen Date of birth: December\u00a03,\u00a01971", header.InnerText);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var words = pdf.GetPages().SelectMany(p => p.GetWords()).ToArray();
        var name = Assert.Single(words.Where(w => w.Text == "Allen"));
        foreach (var token in new[] { "Date", "birth:", "December", "1971" })
            Assert.InRange(Math.Abs(name.BoundingBox.Bottom - Assert.Single(words.Where(w => w.Text == token)).BoundingBox.Bottom), 0, .1);
    }

    [ReviewerLibreOfficeFact]
    public async Task SignedRecordAndReceiptBlockSharePageWhenTheyFit()
    {
        const string source = "Clinical assessment: unchanged.\n/es/ EXAMPLE CLINICIAN\nSigned: 03/29/2025 23:48\n" +
            "Receipt Acknowledged By:\n04/01/2025 14:39   /es/ EXAMPLE NURSE\nPACT RN, BSN, Clinic\n" +
            "04/04/2025 15:33   /es/ EXAMPLE MD STAFF PHYSICIAN";
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([Evidence("Emergency Department", source)]));
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var label = Assert.Single(doc.MainDocumentPart!.Document!.Descendants<Paragraph>().Where(p => p.InnerText == "Receipt Acknowledged By:"));
        Assert.Equal("0", label.ParagraphProperties!.SpacingBetweenLines!.After!.Value);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var page = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains("Receipt Acknowledged By:")));
        Assert.Contains("EXAMPLE CLINICIAN", page.Text);
        Assert.Contains("EXAMPLE NURSE", page.Text);
        Assert.Contains("EXAMPLE MD STAFF PHYSICIAN", page.Text);
        Assert.True(page.Text.IndexOf("EXAMPLE NURSE", StringComparison.Ordinal) < page.Text.IndexOf("EXAMPLE MD", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReceiptCompactionIsBoundedAndPreservesEveryTextRun(bool terminal)
    {
        var body = new Body();
        foreach (var line in new[] { "/es/ CLINICIAN", "Signed: 03/29/2025 23:48", "Receipt Acknowledged By:",
            "04/01/2025 14:39   /es/ EXAMPLE NURSE", "PACT RN, BSN, Clinic", "04/04/2025 15:33   /es/ EXAMPLE MD STAFF PHYSICIAN" })
            body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }),
                new Run(new RunProperties(new FontSize { Val = "24" }), new Text(line))));
        if (!terminal) body.Append(new Paragraph(new Run(new Text("Assessment: further clinical findings"))));
        var before = body.InnerText;
        VeteransReviewerPackageDocxRenderer.CompactReceiptAcknowledgements(body, null);
        Assert.Equal(before, body.InnerText);
        Assert.All(body.Descendants<FontSize>(), font => Assert.Equal("24", font.Val!.Value));
        var label = body.Elements<Paragraph>().ElementAt(2);
        Assert.Equal(terminal ? "0" : "60", label.ParagraphProperties!.SpacingBetweenLines!.After!.Value);
        Assert.Equal(terminal, label.ParagraphProperties.GetFirstChild<KeepNext>() is not null);
        Assert.Equal("60", body.Elements<Paragraph>().First().ParagraphProperties!.SpacingBetweenLines!.After!.Value);
    }

    [Theory]
    [InlineData("April 12,", "1956", "\n")]
    [InlineData("December 3,", "1971", "\r\n")]
    public void DateOfBirthReconstructsExactValueAndKeepsYearInSameAtomicRun(string date, string year, string newline)
    {
        var source = "Date of birth: " + date + newline + year + newline + "Clinical assessment: unchanged";
        Assert.Equal("Date of birth: " + date + " " + year + newline + "Clinical assessment: unchanged",
            VeteransReviewerDateOfBirth.Reconstruct(source));
        using var doc = Render(Evidence("Patient header", source));
        var paragraph = Assert.Single(doc.MainDocumentPart!.Document!.Descendants<Paragraph>()
            .Where(p => p.InnerText.Contains(year, StringComparison.Ordinal)));
        var value = date.Replace(' ', '\u00a0') + "\u00a0" + year;
        Assert.Contains(value, paragraph.InnerText);
        Assert.Contains(paragraph.Descendants<Text>(), t => t.Text.Contains(value, StringComparison.Ordinal));
        Assert.Empty(paragraph.Descendants<Break>());
        Assert.DoesNotContain('\n', paragraph.InnerText);
        Assert.Contains("Date of birth:", doc.MainDocumentPart.Document.InnerText);
    }

    [Theory]
    [InlineData("Follow-up: April 12,\n1956")]
    [InlineData("Date of birth: April 12,\n\n1956")]
    [InlineData("Date of birth: April 12,\n1956 unrelated content")]
    [InlineData("Date of birth: April 12,\n12345")]
    [InlineData("Date of birth:\n\nDecember 3, 1971")]
    [InlineData("Unrelated label:\nDecember 3, 1971")]
    public void DateOfBirthDoesNotJoinOtherCommaLinesOrCrossBoundaries(string source)
    {
        Assert.Equal(source, VeteransReviewerDateOfBirth.Reconstruct(source));
    }

    [Fact]
    public void KnownNoteAndPainQuestionLabelsUseFullWidthParagraphs()
    {
        string[] lines = ["STANDARD TITLE: ACUPUNCTURE NOTE",
            "The patient was asked the following questions: During the past 24 hours, how much has your pain interfered? Pain number from 0-10: 6",
            "Post treatment Numeric Pain Rating Scale: Pain number from 0-10: 5"];
        using var doc = Render(Evidence("Acupuncture", string.Join('\n', lines)));
        var body = doc.MainDocumentPart!.Document!.Body!;
        foreach (var line in lines)
            Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == line);
        Assert.DoesNotContain(body.Descendants<Table>(), t => lines.Any(line => t.InnerText.Contains(line.Split(':')[0])));
    }

    [Theory]
    [InlineData("Emergency Department", "ER Staff Assessment")]
    [InlineData("Physical Therapy", "Physical Therapy Consult")]
    public void ContainedProjectionRendersEncounterOnceAndPreservesSource(string full, string partial)
    {
        using var first = new NativePage(1224, 1584);
        first.Line("Opening clinical evidence.", 50);
        using var last = new NativePage(1224, 1584);
        last.Line("Assessment and unique treatment response.", 50);
        var a = Evidence(full, parent: "same-source", pages: [first.Page(pageNumber: 504), last.Page(pageNumber: 505)]);
        var b = Evidence(partial, parent: "same-source", pages: [last.Page(pageNumber: 505)]);
        var original = b.PrintablePages[0].Content.ToArray();
        using var doc = Render(b, a);
        var headings = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2").Select(p => p.InnerText).ToArray();
        Assert.Contains(full, headings);
        Assert.DoesNotContain(partial, headings);
        Assert.Equal(2, doc.MainDocumentPart.ImageParts.Count());
        Assert.Equal(original, b.PrintablePages[0].Content.ToArray());
    }

    [Fact]
    public void ContainmentUsesGlyphIdentityForClippedBoundaryAndRetainsNewFindings()
    {
        using var page = new NativePage(1224, 1584);
        page.Line("Opening title", 50);
        page.Line("Clinical observation", 80);
        var full = page.Page(pageNumber: 10);
        var clipped = new PrintableArtifactPage { PageNumber = 10, ContentType = "image/png", Content = new byte[] { 1 },
            TextGeometry = full.TextGeometry! with { Glyphs = full.TextGeometry.Glyphs.Where(g => g.Baseline > 60).ToArray() } };
        var a = Evidence("full", parent: "source", pages: [full]);
        var b = Evidence("clipped", parent: "source", pages: [clipped]);
        Assert.Equal(new[] { a }, VeteransReviewerSourceMembership.Select(Details([a, b]), [a, b]));
        var differentSource = Evidence("different-source", parent: "other", pages: [clipped]);
        Assert.Equal(2, VeteransReviewerSourceMembership.Select(Details([a, differentSource]), [a, differentSource]).Count);
        using var newPage = new NativePage(1224, 1584);
        newPage.Line("Clinical observation with new August treatment response", 80);
        var august = Evidence("August follow-up", parent: "source", pages: [newPage.Page(pageNumber: 11)]);
        Assert.Equal(2, VeteransReviewerSourceMembership.Select(Details([a, august]), [a, august]).Count);
    }

    [Fact]
    public void AppendixDContainsStatementsWhileClinicalMessageAndClarificationRemainAvailable()
    {
        var message = Evidence("Primary Care Secure Messaging - Lidocaine Patches", "Clinical message body", appendix: "LayEvidence");
        var clarification = Evidence("Veteran Lay Clarification_AFO Fit and Falls", "Clarification body", appendix: "LayEvidence", type: "file");
        using var doc = Render(message, clarification,
            Evidence("Spousal Statement", "Spousal body", appendix: "LayEvidence", type: "file"),
            Evidence("Veteran Personal Statement", "Veteran body", appendix: "LayEvidence", type: "file"));
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        var lay = text[text.LastIndexOf("Appendix D — Lay Evidence", StringComparison.Ordinal)..];
        lay = lay[..lay.IndexOf("Appendix E —", StringComparison.Ordinal)];
        Assert.Contains("Spousal body", lay);
        Assert.Contains("Veteran body", lay);
        Assert.DoesNotContain("Clinical message body", lay);
        Assert.DoesNotContain("Clarification body", lay);
        Assert.Contains("Clinical message body", text);
        Assert.Contains("Additional Evidence", text);
        Assert.Contains("Clarification body", text);
    }

    [Fact]
    public void MedsPrefixExcludesOnlyBoundedHistoricalTable()
    {
        using var page = new NativePage(1224, 1584);
        page.Line("Reports MEDS help relieve pain.", 40);
        page.Line("MEDS: Active Outpatient Medications (including Supplies):", 70);
        page.Line("1) HISTORICAL TABLE MEDICATION ACTIVE", 90);
        page.Line("OBJECTIVE:", 120);
        page.Line("Gait impairment remains clinically relevant.", 140);
        var source = page.Page();
        var original = source.Content.ToArray();
        var selected = VeteransReviewerNativeEvidencePage.SuppressHistoricalMedications([source], out var omitted);
        Assert.Equal(2, omitted);
        var text = string.Join(" ", selected.SelectMany(VeteransReviewerNativeProse.Lines).Select(r => r.Text));
        Assert.Contains("Reports MEDS help relieve pain.", text);
        Assert.Contains("Gait impairment", text);
        Assert.DoesNotContain("HISTORICAL TABLE", text);
        Assert.Equal(original, source.Content.ToArray());
        using var doc = Render(Evidence("PT note", parent: "source", pages: [source]));
        Assert.Contains("Historical medication table omitted", doc.MainDocumentPart!.Document!.InnerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PowerFormUsesOneBodyFontAndSuppressesInternalIdentifiersWithoutInventingCells()
    {
        const string source = "PowerForm Textual Rendition Notes\nMRN: 116732263000001\nFIN: 219435629\n" +
            "1207832630 Veterans ID (ICN): 1022399772V106425DOD ID (EDIPI):\n" +
            "Assessment\nPhysical Therapy Evaluation/Assessment :   Examine fingernails.\n" +
            "Long Term Goal 1 Long Term Goal 2\nGoal :   I and compliant\nwith HEP.\nimproved ability\nStatus :   Goal met Not met\n09/25/2026   11:30   EDT";
        using var doc = Render(Evidence("PowerForm", source, type: "file"));
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Elements<Paragraph>().SkipWhile(p => p.InnerText != "PowerForm Textual Rendition Notes")
            .TakeWhile(p => p.ParagraphProperties?.ParagraphStyleId?.Val != "Heading1").ToArray();
        Assert.NotEmpty(paragraphs);
        Assert.All(paragraphs.SelectMany(p => p.Elements<Run>()), r =>
        {
            Assert.Equal("24", r.RunProperties!.FontSize!.Val!.Value);
            Assert.Equal(VeteransReviewerFonts.Body, r.RunProperties.RunFonts!.Ascii!.Value);
        });
        Assert.Contains("Examine fingernails.", body.InnerText);
        Assert.DoesNotContain("Patient identifier:", body.InnerText);
        Assert.DoesNotContain("116732263000001", body.InnerText);
        Assert.DoesNotContain("5DOD", body.InnerText);
        Assert.Contains("Status :   Goal met Not met", body.InnerText);
        Assert.Empty(body.Elements<Table>());
    }

    [Theory]
    [InlineData("During the past 24 hours, how much has your pain interfered?", "Pain number from 0-10: 6")]
    [InlineData("Post treatment Numeric Pain Rating Scale:", "Pain number from 0-10: 6")]
    [InlineData("Rate your satisfaction:", "number from 0-10:")]
    [InlineData("Your pain improved. How satisfied are you?", "number from 0-10:")]
    public void PainScaleLabelRequiresExplicitPainContext(string context, string expected)
    {
        var output = VeteransReviewerNativeProse.JoinDetachedPainScaleLines([context, "number from 0-10:", "6"]);
        Assert.Contains(expected, string.Join(" ", output));
        if (expected == "number from 0-10:") Assert.DoesNotContain("Pain number", string.Join(" ", output));
    }

    [Fact]
    public void OnlyKnownFragmentedFormLabelsAreJoined()
    {
        var result = VeteransReviewerNativeProse.JoinKnownFormLabels(
            ["STANDARD", "TITLE: PHYSICAL THERAPY NOTE", "The patient was", "asked the", "following", "questions:",
             "Post treatment", "Numeric Pain", "Rating Scale:", "Other", "short", "lines"]);
        Assert.Equal(new[] { "STANDARD TITLE: PHYSICAL THERAPY NOTE", "The patient was asked the following questions:",
            "Post treatment Numeric Pain Rating Scale:", "Other", "short", "lines" }, result);
    }

    [Fact]
    public void AlignedNarrativeEligibilityRetainsFieldsTablesScalarsAndSignatures()
    {
        using var first = new NativePage(1224, 1584);
        first.Line("The Veteran reports chronic symptoms after prolonged standing", 50, x: 45);
        first.Line("and walking despite treatment with activity modification.", 65, x: 45);
        first.Line("The clinician reviewed the home exercise program and", 80, x: 45);
        first.Line("recommended continuation with regular follow-up visits.", 95, x: 45);
        first.Line("Assessment: Symptoms improve following the prescribed treatment plan.", 125, x: 45);
        first.Line("Plan: Continue exercises with reassessment at the next visit.", 140, x: 45);
        first.Line("Test", 170, x: 45); first.Line("Result", 170, x: 230); first.Line("Ref range", 170, x: 360);
        first.Line("Glucose", 185, x: 45); first.Line("100", 185, x: 230); first.Line("70-110", 185, x: 360);
        first.Line("6", 200, x: 45);
        first.Line("Caller's Comments: Symptoms worsened after prolonged walking yesterday.", 215, x: 45);
        first.Line("/es/ SOURCE CLINICIAN", 230, x: 45);
        first.Line("PHYSICIAN", 245, x: 45);
        using var last = new NativePage(1224, 1584);
        last.Line("The patient understands the proposed treatment plan and", 50, x: 45);
        last.Line("will return for further assessment after physical therapy.", 65, x: 45);
        var paragraphs = VeteransReviewerNativeProse.Reconstruct([first.Page(), last.Page(pageNumber: 2)]);
        Assert.NotNull(paragraphs);
        Assert.Contains(paragraphs!, p => p.StartsWith("Assessment:") && !p.Contains("Plan:"));
        Assert.Contains(paragraphs!, p => p.StartsWith("Plan:"));
        Assert.Contains(paragraphs!, p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^Glucose {2,}100 {2,}70-110$"));
        Assert.Contains("6", paragraphs!);
        Assert.Contains(paragraphs!, p => p.StartsWith("Caller's Comments:") && !p.Contains("/es/"));
        Assert.Contains("/es/ SOURCE CLINICIAN PHYSICIAN", paragraphs!);
    }

    [Fact]
    public void CoverDisplaysSourceGroundedClaimAndExactBasisWithoutInternalId()
    {
        var initial = Details([Evidence("clinical note", "Source findings")]);
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = initial.PackageDetails, Artifacts = initial.Artifacts, ArtifactContents = initial.ArtifactContents,
            MedicalOpinionRequested = new() { OpinionText = "Determine whether the Veteran's Lumbar degenerative disc disease (L3-L5) is at least as likely as not (50 percent or greater probability) proximately due to or the result of the Veteran's service-connected Bilateral pes planus. If causation is not established, determine aggravation." }
        };
        using var doc = WordprocessingDocument.Open(new MemoryStream(VeteransReviewerPackageDocxRenderer.Render(details)), false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Claim type: Secondary service connection", text);
        Assert.Contains("Claimed condition: Lumbar degenerative disc disease (L3-L5)", text);
        Assert.Contains("Basis: Bilateral pes planus", text);
        Assert.DoesNotContain("basis-lumbar-flat-feet", text);
    }

    [ReviewerLibreOfficeFact]
    public async Task SourceTitleAndSignatureReflowAndAdditionalHeadingStaysWithEvidence()
    {
        using var title = new NativePage(1224, 1584);
        title.Line("CCC: CLINICAL TRIAGE", 730, font: "Bitter-Bold", size: 16);
        using var note = new NativePage(1224, 1584);
        note.Line("Details", 55, font: "SourceSansPro");
        note.Line("Date entered: June 22, 2026", 80, font: "SourceSansPro");
        note.Line("Note", 105, font: "SourceSansPro");
        note.Line("Date of birth: April 12,", 120, font: "SourceSansPro");
        note.Line("1956", 135, font: "SourceSansPro");
        note.Line("The Veteran reports worsening back pain after prolonged standing", 150, x: 45);
        note.Line("and walking despite activity modification and prescribed treatment.", 165, x: 45);
        note.Line("He reports recurrent falls while using the prescribed ankle brace", 195, x: 45);
        note.Line("and continues to use a cane for stability during ambulation.", 210, x: 45);
        note.Line("The clinician reviewed gait safety and the home exercise program", 240, x: 45);
        note.Line("and recommended continued physical therapy with reassessment.", 255, x: 45);
        using var signature = new NativePage(1224, 1584);
        signature.Line("/es/ EXAMPLE CLINICIAN", 55);
        signature.Line("Signed: 06/22/2026 14:30", 80);
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([
            Evidence("Clinical Triage", pages: [title.Page(), note.Page(pageNumber: 2), signature.Page(pageNumber: 3)]),
            Evidence("Veteran Lay Clarification_AFO", "Verified clarification body.", appendix: "LayEvidence", type: "file")]));
        using (var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false)) Assert.Empty(doc.MainDocumentPart!.ImageParts);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var clinical = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains("The Veteran reports worsening")));
        Assert.Contains("Clinical Triage", clinical.Text);
        Assert.Contains("Date entered", clinical.Text);
        Assert.Contains("/es/ EXAMPLE CLINICIAN", clinical.Text);
        Assert.Contains("Signed:", clinical.Text);
        Assert.Contains("06/22/2026 14:30", clinical.Text);
        var words = clinical.GetWords().ToArray();
        var month = Assert.Single(words.Where(w => w.Text == "April"));
        var year = Assert.Single(words.Where(w => w.Text == "1956"));
        Assert.InRange(Math.Abs(month.BoundingBox.Bottom - year.BoundingBox.Bottom), 0, .1);
        Assert.DoesNotContain(pdf.GetPages(), p => p.Text.Contains("Date entered") && !p.Text.Contains("The Veteran reports worsening"));
        var additional = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains("Additional Evidence")));
        Assert.Contains("Veteran Lay Clarification_AFO", additional.Text);
        Assert.Contains("Verified clarification body.", additional.Text);
    }

    [Fact]
    public void PowerFormRemovesOnlyCompleteRepeatedHeadersAndKeepsUnresolvedGoalOrder()
    {
        const string header = "Patient Name: EXAMPLE Admit: 8/22/2026\nMRN: 116732263000001 Discharge:\nFIN: 219435629 Admitting:\nSex/DOB/Age: Male 70 years\nDOD ID (EDIPI): 1207832630 1022399772V106425Veterans ID (ICN):\nPowerForm Textual Rendition Notes";
        const string unresolved = "Long Term Goal 1 Long Term Goal 2\nGoal : I with HEP\nimproved symptoms\nStatus : Goal met Not met\nDate Met : 08/28/2026 EDT\nProvider - 09/25/26 11:30 EDT";
        var lines = VeteransReviewerPowerForm.Lines(header + "\n*Progress Note/\n Re-Eval Complete : Yes\n" + unresolved +
            "\n1598645564 Print Date/Time: 9/25/2026 11:59 CDTReport Request ID: Page 1 of 6\n" + header + "\nNew clinical findings.");
        var text = string.Join("\n", lines);
        Assert.Contains(unresolved, text);
        Assert.Contains("*Progress Note/Re-Eval Complete: Yes", text);
        Assert.Single(lines.Where(l => l == "PowerForm Textual Rendition Notes"));
        Assert.DoesNotContain("1598645564", text);
        Assert.Contains("New clinical findings.", text);
        Assert.Contains("Provider - 09/25/26 11:30 EDT", text);
    }

    [Fact]
    public void LayNativeStatementHasNoReviewerSourcePageLabel()
    {
        using var page = new NativePage(1224, 1584);
        page.Line("Personal statement and signature.", 50);
        using var doc = Render(Evidence("Veteran Personal Statement", pages: [page.Page()], appendix: "LayEvidence", type: "file"));
        Assert.DoesNotContain("Source Page 1", doc.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Single(doc.MainDocumentPart.ImageParts);
    }

    [Fact]
    public void InlinePainQuestionKeepsDetachedValueAndDoesNotRelabelAnotherScale()
    {
        var result = VeteransReviewerNativeProse.JoinDetachedPainScaleLines(
            ["How severe is your pain? number from 0-10:", "6", "How satisfied are you? number from 0-10: 8"]);
        Assert.Equal(new[] { "How severe is your pain? Pain number from 0-10: 6", "How satisfied are you? number from 0-10: 8" }, result);
    }

    [Fact]
    public void LaySupplementContainsOnlyMissingTypedSentencesAndStaysWithinStatement()
    {
        const string visible = "I have experienced chronic back pain for many years and describe my daily limitations here.";
        const string clipped = "I ask the reviewer to consider whether my service-connected Bilateral pes planus caused or aggravated my lumbar condition.";
        const string certification = "I certify that the statements above are true and correct to the best of my knowledge and belief.";
        using var page = new NativePage(1224, 1584);
        page.Line(visible, 50, size: 8);
        page.Line("I ask the reviewer to consider whether my service-connected", 75, size: 8);
        page.Line("Bilateral pes planus", 90, size: 8);
        var source = page.Page(pageNumber: 1);
        var statement = Evidence("Veteran Personal Statement.docx", visible + clipped + certification + "Signer Signature: handwritten date",
            pages: [source], appendix: "LayEvidence", type: "file");
        Assert.Equal(new[] { clipped, certification }, VeteransReviewerLayTextSupplement.Get(statement));
        var details = Details([statement]);
        Assert.Single(VeteransReviewerSourceMembership.Select(details, [statement]));
        using var doc = Render(statement);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToArray();
        var supplement = Array.FindIndex(paragraphs, p => p.InnerText == "Veteran Personal Statement — Text Supplement");
        Assert.True(supplement >= 0);
        Assert.Equal("Heading3", paragraphs[supplement].ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Contains("Source: Independently stored text rendition of the same statement", paragraphs[supplement + 1].InnerText);
        Assert.Equal(clipped, paragraphs[supplement + 2].InnerText);
        Assert.Equal(certification, paragraphs[supplement + 3].InnerText);
        Assert.StartsWith("Reviewer note:", paragraphs[supplement + 4].InnerText);
        Assert.DoesNotContain("Signer Signature:", doc.MainDocumentPart.Document.InnerText);
        using var embedded = Assert.Single(doc.MainDocumentPart.ImageParts).GetStream();
        using var copy = new MemoryStream(); embedded.CopyTo(copy);
        Assert.Equal(source.Content.ToArray(), copy.ToArray());
    }

    [Fact]
    public void FullyVisibleLayStatementDoesNotReceiveDuplicateSupplement()
    {
        const string text = "I certify that the statements above are true and correct to the best of my knowledge and belief.";
        using var page = new NativePage(1224, 1584);
        page.Line(text, 50, size: 8);
        Assert.Null(VeteransReviewerLayTextSupplement.Get(Evidence("Statement.docx", text + "Signature: preserved image",
            pages: [page.Page()], appendix: "LayEvidence", type: "file")));
    }

    [ReviewerLibreOfficeFact]
    public async Task FullPageLayImagePreservesBottomTextAndSignatureRegionThroughPdfConversion()
    {
        using var page = new NativePage(1224, 1584);
        page.Line("Full page statement", 40);
        page.Line("Final certification text", 757);
        page.Line("SYNTHETIC SIGNATURE REGION", 782);
        var source = page.Page(pageNumber: 1);
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([
            Evidence("Veteran Personal Statement", pages: [source], appendix: "LayEvidence", type: "file")]));
        using (var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false))
        {
            using var embedded = Assert.Single(doc.MainDocumentPart!.ImageParts).GetStream();
            using var copy = new MemoryStream(); embedded.CopyTo(copy);
            Assert.Equal(source.Content.ToArray(), copy.ToArray());
            Assert.Empty(doc.MainDocumentPart.Document!.Descendants<DocumentFormat.OpenXml.Drawing.SourceRectangle>());
        }
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var pdfPage = Assert.Single(pdf.GetPages().Where(p => p.NumberOfImages > 0));
        var image = Assert.Single(pdfPage.GetImages());
        Assert.InRange(image.BoundingBox.Bottom, 60, pdfPage.Height);
        Assert.True(image.BoundingBox.Top < pdfPage.Height - 50);
        using var bitmap = image.TryGetPng(out var png) ? SkiaSharp.SKBitmap.Decode(png) : SkiaSharp.SKBitmap.Decode(image.RawMemory.Span);
        Assert.NotNull(bitmap);
        Assert.InRange(bitmap.Width / (double)bitmap.Height, 1224d / 1584 - .002, 1224d / 1584 + .002);
        var bottomInk = 0;
        for (var y = (int)(bitmap.Height * .975); y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Red < 220 && pixel.Green < 220 && pixel.Blue < 220) bottomInk++;
        }
        Assert.True(bottomInk > 50, "Bottom signature region must survive whole-page placement and PDF conversion.");
    }
}
