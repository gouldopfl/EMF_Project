using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using SkiaSharp;

namespace EMF.Tests;

public sealed class VeteransReviewerNativeEvidencePageTests
{
    [Theory]
    [InlineData("SLEEP MED TELEPHONE NOTE")]
    [InlineData("SLEEP MED REMOTE PAP FOLLOW-UP NOTE")]
    [InlineData("SLEEP MED PAP CLINIC NOTE")]
    [InlineData("SLEEP MED PAP SET-UP CONSULT RESULT")]
    [InlineData("SLEEP MED SLEEP SPECIALIST INITIAL CONSULTATION NOTE")]
    public void EquivalentOpeningTitleIsSuppressedWithoutChangingBodyScaleOrMetadata(string title)
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line(title, 60, font: "Bitter-Bold", size: 16, x: 16);
        fixture.Line("LOCAL TITLE: " + title, 100, size: 12);
        fixture.Line("STANDARD TITLE: SLEEP MEDICINE NOTE", 120, size: 12);
        fixture.Line("PHYSICAL EXAM:", 160, size: 12);
        fixture.Line("The original clinical findings remain unchanged.", 180, size: 12);
        var page = fixture.Page();
        var original = page.Content.ToArray();
        var baseline = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true,
            title.ToLowerInvariant().Replace("-", " ") + " — Continued");
        Assert.Contains(result.Changes, c => c.StartsWith("Suppressed duplicate opening"));
        var lines = result.Regions.SelectMany(r => r.RenderedLines).ToArray();
        Assert.DoesNotContain(title, lines);
        Assert.Contains("LOCAL TITLE: " + title, lines);
        Assert.Contains("STANDARD TITLE: SLEEP MEDICINE NOTE", lines);
        Assert.Contains("PHYSICAL EXAM:", lines);
        using var before = SKBitmap.Decode(baseline.Content.Span);
        using var after = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        var titleRegion = result.Regions.First();
        for (var y = titleRegion.TargetBottom; y < before.Height; y++)
        for (var x = 0; x < before.Width; x++)
            Assert.Equal(before.GetPixel(x, y), after.GetPixel(x, y));
        Assert.Equal(original, page.Content.ToArray());
    }

    [Fact]
    public void ShortMetadataOnlyOpeningUsesRegularValueGlyphsForBodySize()
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line("SLEEP MED TELEPHONE NOTE", 552, font: "Bitter-Bold", size: 16, x: 16);
        fixture.Line("Details", 588, font: "Bitter-Bold", size: 14, x: 45);
        fixture.Line("Date entered:", 616, font: "SourceSansPro-Bold", size: 12, x: 45);
        fixture.Line("April 17, 2025", 616, font: "SourceSansPro-Regular", size: 12, x: 146);
        fixture.Line("Location:", 634, font: "SourceSansPro-Bold", size: 12, x: 45);
        fixture.Line("SOURCE MEDICAL CENTER", 634, font: "SourceSansPro-Regular", size: 12, x: 120);
        fixture.Line("Note", 720, font: "Bitter-Bold", size: 14, x: 45);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true, "SLEEP MED TELEPHONE NOTE");
        Assert.Contains(result.Changes, c => c.StartsWith("Suppressed duplicate"));
        Assert.Equal(2, result.Changes.Count(c => c.StartsWith("Restrained native section") && c.Contains("to 12.0pt")));
        Assert.Contains(result.Regions.SelectMany(r => r.RenderedLines), l => l.Contains("April 17, 2025"));
    }

    [Theory]
    [InlineData("SLEEP MED TELEPHONE FOLLOW-UP NOTE")]
    [InlineData("SLEEP MED TELE PHONE NOTE")]
    [InlineData("SLEEP MED TELEPHONE NOTE ADDENDUM")]
    [InlineData("LOCAL TITLE: SLEEP MED TELEPHONE NOTE")]
    [InlineData("STANDARD TITLE: SLEEP MED TELEPHONE NOTE")]
    public void NonEquivalentOpeningAndMetadataAreRetained(string heading)
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line(heading, 60, font: "Bitter-Bold", size: 16);
        fixture.Line("The clinical body remains readable.", 100, size: 12);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true, "SLEEP MED TELEPHONE NOTE");
        Assert.DoesNotContain(result.Changes, c => c.StartsWith("Suppressed duplicate"));
        Assert.Contains(heading, result.Regions.SelectMany(r => r.RenderedLines));
    }

    [Theory]
    [InlineData("DATA DOWNLOAD FROM PAP DEVICE:")]
    [InlineData("CURRENT PROBLEMS/ISSUES:")]
    [InlineData("EDUCATION:")]
    [InlineData("SLEEP STAFF COMMENTS:")]
    [InlineData("FOLLOW UP:")]
    [InlineData("ADDITIONAL MANAGEMENT/PLAN:")]
    [InlineData("ASSESSMENT/PLAN:")]
    [InlineData("PHYSICAL EXAM:")]
    public void OversizedInternalHeadingIsRetainedAtBodySize(string heading)
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line(heading, 60, font: "Bitter-Bold", size: 18);
        fixture.Line("Normal clinical text with exact numbers: 32.8, 8.6 and 4.2.", 100, size: 12);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true);
        Assert.Contains(heading, result.Regions.SelectMany(r => r.RenderedLines));
        Assert.Contains(result.Changes, c => c.Contains("from 18.0pt to 12.0pt"));
        using var bitmap = SKBitmap.Decode(result.Content.Span);
        var region = result.Regions.First();
        var inkRows = Enumerable.Range(region.TargetTop, region.TargetBottom - region.TargetTop)
            .Count(y => Enumerable.Range(0, bitmap.Width).Any(x => bitmap.GetPixel(x, y) != SKColors.White));
        Assert.InRange(inkRows, 30, 43); // 12pt * .8 fixture glyph height * 300/72 dpi.
    }

    [Fact]
    public void WrappedOpeningTitleIsSuppressedButLaterMatchingHeadingRemains()
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line("SLEEP MED SLEEP SPECIALIST", 60, font: "Bitter-Bold", size: 16);
        fixture.Line("INITIAL CONSULTATION NOTE", 80, font: "Bitter-Bold", size: 16);
        fixture.Line("LOCAL TITLE: SLEEP MEDICINE NOTE", 120, size: 12);
        fixture.Line("Clinical observations retain exact wording and values.", 150, size: 12);
        fixture.Line("SLEEP MED SLEEP SPECIALIST INITIAL CONSULTATION NOTE", 210, font: "Bitter-Bold", size: 16);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true,
            "sleep med sleep specialist initial consultation note — Continued");
        Assert.Equal(2, result.Changes.Count(c => c.StartsWith("Suppressed duplicate")));
        var lines = result.Regions.SelectMany(r => r.RenderedLines).ToArray();
        Assert.DoesNotContain("SLEEP MED SLEEP SPECIALIST", lines);
        Assert.DoesNotContain("INITIAL CONSULTATION NOTE", lines);
        Assert.Contains("SLEEP MED SLEEP SPECIALIST INITIAL CONSULTATION NOTE", lines);
        Assert.Contains("LOCAL TITLE: SLEEP MEDICINE NOTE", lines);
    }

    [Fact]
    public void SeparatedHeadingsAreNotCombinedIntoAnEquivalentOpeningTitle()
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line("SLEEP MED SLEEP SPECIALIST", 60, font: "Bitter-Bold", size: 16);
        fixture.Line("INITIAL CONSULTATION NOTE", 220, font: "Bitter-Bold", size: 16);
        fixture.Line("Original clinical text remains in source order.", 260, size: 12);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true,
            "SLEEP MED SLEEP SPECIALIST INITIAL CONSULTATION NOTE");
        Assert.DoesNotContain(result.Changes, c => c.StartsWith("Suppressed duplicate"));
        AssertAllInk(page, result);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(12)]
    public void BodySizedClinicalHeadingsKeepAllSourcePixels(double size)
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        fixture.Line("DATA DOWNLOAD FROM PAP DEVICE:", 60, size: size);
        fixture.Line("Residual AHI: 5.8 events per hour", 80, size: size);
        fixture.Line("CURRENT PROBLEMS/ISSUES:", 115, size: size);
        fixture.Line("Patient reports clinical findings unchanged.", 135, size: size);
        fixture.Line("/es/ SOURCE CLINICIAN", 175, size: size);
        fixture.Line("Signed: 10/27/2025 08:11", 195, size: size);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true, "SLEEP MED REMOTE PAP FOLLOW-UP NOTE");
        Assert.DoesNotContain(result.Changes, c => c.StartsWith("Restrained") || c.StartsWith("Suppressed"));
        AssertAllInk(page, result);
        AssertStructuredGeometry(page, result);
        Assert.Contains("DATA DOWNLOAD FROM PAP DEVICE:", result.Regions.SelectMany(r => r.RenderedLines));
        Assert.Contains("CURRENT PROBLEMS/ISSUES:", result.Regions.SelectMany(r => r.RenderedLines));
    }

    [Fact]
    public void TelephoneContinuation_ReflowsInlineMetricAndLowercaseLabelWithoutChangingWordsOrInk()
    {
        using var fixture = new NativePage(2550, 3300, 612, 792);
        var lines = new[] {
            "Pt had surgery his in last 30 days, residual AHI has been 32.8 CAI: 8.6,",
            "OAI:",
            "4.2, HI:20 and avg unintended leaks have been 46.4 and med leaks 53 LPM",
            "with avg", "mask fit of 60%." };
        for (var i = 0; i < lines.Length; i++) fixture.Line(lines[i], 52.6 + i * 17.826, size: 12, x: 45);
        var second = new[] {
            "When pt was asked what mask they are currently using , the pt was told the",
            "following:F30med and fisher and paykel black forma full face mask . PT",
            "said", "since surgery- 3 weeks ago- he is breathing normally--pt has not tried f40",
            "and", "pt will trial to see if numbers have gone done and compare to the fisher",
            "and", "paykel evora mask. PT will have 1 month f/u." };
        for (var i = 0; i < second.Length; i++) fixture.Line(second[i], 159.56 + i * 17.826, size: 12, x: 45);
        fixture.Line("/es/ SOURCE CLINICIAN", 320, size: 12, x: 45);
        fixture.Line("Signed: 04/22/2025 09:35", 355.6, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var prose = result.Regions.Where(r => r.Kind == "Narrative").ToArray();
        Assert.Equal(2, prose.Length);
        Assert.Equal(Words(lines.Concat(second)), Words(prose.SelectMany(r => r.RenderedLines)));
        Assert.DoesNotContain(prose.SelectMany(r => r.RenderedLines), l => l is "OAI:" or "PT" or "said" or "and");
        AssertAllInk(page, result);
        AssertStructuredGeometry(page, result);
    }

    [Fact]
    public void MetricLabelAfterParagraphBreakRemainsStructured()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("The preceding clinical prose ends with a comma,", 50, size: 12, x: 45);
        fixture.Line("OAI:", 86, size: 12, x: 45);
        fixture.Line("4.2, HI:20 and further clinical findings", 104, size: 12, x: 45);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true);
        Assert.Contains(result.Regions, r => r.Kind == "Structured" && r.SourceLines.Contains("OAI:"));
        AssertStructuredGeometry(fixture.Page(), result);
    }

    [Fact]
    public void LongClinicalLabelsDoNotTurnNumericDataAndShortValuesIntoProse()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("Compliance: 100 % > 4 hrs all night used (Goal of effective therapy", 50, size: 12, x: 59.4);
        fixture.Line("is 70% or above.)", 68, size: 12, x: 59.4);
        fixture.Line("Average time in large leak/day: NA", 86, size: 12, x: 59.4);
        fixture.Line("Average device mean pressure: 12.8 cmH20", 104, size: 12, x: 59.4);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        Assert.All(result.Regions, r => Assert.Equal("Structured", r.Kind));
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Fact]
    public void IndentedTelephoneProseReflowsMarginOrphanAndRetainsAddendumAndSignatures()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("/es/ SOURCE CLINICIAN", 32, size: 12, x: 45);
        fixture.Line("Signed: 04/17/2025 09:40", 50, size: 12, x: 45);
        fixture.Line("04/22/2025 ADDENDUM STATUS: COMPLETED", 86, size: 12, x: 45);
        fixture.Line("pt is having some pap issues, especially after his nasal surgery and", 104, size: 12, x: 73.8);
        fixture.Line("has", 122, size: 12, x: 45);
        fixture.Line("some questions about his machine he would like a call back from.", 140, size: 12, x: 73.8);
        fixture.Line("/es/ SOURCE CLINICIAN", 176, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var prose = Assert.Single(result.Regions.Where(r => r.Kind == "Narrative"));
        Assert.DoesNotContain("has", prose.RenderedLines);
        Assert.Equal(Words(prose.SourceLines), Words(prose.RenderedLines));
        Assert.Contains(result.Regions, r => r.Kind == "Structured" && r.SourceLines.Any(l => l.Contains("ADDENDUM")));
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Fact]
    public void SourceNumericTypoIsPreservedVerbatimDuringNativeProseReflow()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("Weight loss of about 8200 pounds after his bariatric surgery. Has lost", 50, size: 12, x: 45);
        fixture.Line("about 50", 68, size: 12, x: 45);
        fixture.Line("pounds more recently with medications", 86, size: 12, x: 45);
        var page = fixture.Page();
        var original = page.Content.ToArray();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        Assert.Contains("8200", Words(result.Regions.SelectMany(r => r.RenderedLines)));
        Assert.DoesNotContain("82", Words(result.Regions.SelectMany(r => r.RenderedLines)));
        Assert.Equal(original, page.Content.ToArray());
        AssertAllInk(page, result);
    }

    [Fact]
    public void HistoricalMedicationExclusionSpansPagesAndRetainsSurroundingClinicalPixels()
    {
        using var first = new NativePage(1224, 1584);
        first.Line("ALLERGIES:", 50, size: 12, x: 45);
        first.Line("PENICILLIN", 68, size: 12, x: 45);
        first.Line("MEDICATIONS:", 700, size: 12, x: 45);
        first.Line("Active Outpatient Medications (including Supplies):", 718, size: 12, x: 45);
        using var middle = new NativePage(1224, 1584);
        middle.Line("Outpatient Medications                         Status", 50, size: 12, x: 45);
        for (var i = 1; i <= 17; i++) middle.Line($"{i}) MEDICATION {i} TAKE AS DIRECTED              ACTIVE", 68 + i * 18, size: 12, x: 45);
        using var last = new NativePage(1224, 1584);
        last.Line("Non-VA Medications                             Status", 50, size: 12, x: 45);
        last.Line("1) Non-VA MEDICATION TAKE AS DIRECTED            ACTIVE", 68, size: 12, x: 45);
        last.Line("19 Total Medications", 86, size: 12, x: 45);
        last.Line("Columbia Suicide Severity Rating Scale (C-SSRS) screener.", 122, size: 12, x: 45);
        last.Line("PHYSICAL EXAM:", 158, size: 12, x: 45);
        last.Line("General: no acute distress", 176, size: 12, x: 45);
        last.Line("ASSESSMENT/PLAN:", 212, size: 12, x: 45);
        last.Line("/es/ SOURCE CLINICIAN", 248, size: 12, x: 45);
        last.Line("01/25/2024 ADDENDUM STATUS: COMPLETED", 284, size: 12, x: 45);
        var pages = new[] { first.Page(pageNumber: 2012), middle.Page(pageNumber: 2013), last.Page(pageNumber: 2014) };
        var originals = pages.Select(p => p.Content.ToArray()).ToArray();
        var result = VeteransReviewerNativeEvidencePage.SuppressHistoricalMedications(pages, out var count);
        Assert.Equal(23, count);
        Assert.Equal(new[] { 2012, 2014 }, result.Select(p => p.PageNumber));
        var text = string.Concat(result.SelectMany(p => p.TextGeometry!.Glyphs).Select(g => g.Text));
        Assert.DoesNotContain("MEDICATION", text, StringComparison.OrdinalIgnoreCase);
        foreach (var token in new[] { "PENICILLIN", "C-SSRS", "PHYSICAL", "ASSESSMENT/PLAN", "/es/", "ADDENDUM" })
            Assert.Contains(token, text);
        for (var i = 0; i < pages.Length; i++) Assert.Equal(originals[i], pages[i].Content.ToArray());
        // Every retained clinical glyph still points to the identical source pixels.
        foreach (var p in result)
        {
            using var before = SKBitmap.Decode(pages.Single(x => x.PageNumber == p.PageNumber).Content.Span);
            using var after = SKBitmap.Decode(p.Content.Span);
            foreach (var g in p.TextGeometry!.Glyphs)
                for (var y = (int)(g.Top * 2); y < (int)(g.Bottom * 2); y++)
                for (var x = (int)(g.Left * 2); x < (int)(g.Right * 2); x++)
                    Assert.Equal(before.GetPixel(x, y), after.GetPixel(x, y));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalMedicationExclusionDoesNotGuessAcrossMissingBoundaryOrPage(bool missingPage)
    {
        using var first = new NativePage(1224, 1584);
        first.Line("Active Outpatient Medications (including Supplies):", 50, size: 12, x: 45);
        first.Line("1) MEDICATION TAKE AS DIRECTED ACTIVE", 68, size: 12, x: 45);
        using var last = new NativePage(1224, 1584);
        last.Line(missingPage ? "PHYSICAL EXAM:" : "Unrecognized continuation", 50, size: 12, x: 45);
        var pages = new[] { first.Page(pageNumber: 1), last.Page(pageNumber: missingPage ? 3 : 2) };
        Assert.Same(pages, VeteransReviewerNativeEvidencePage.SuppressHistoricalMedications(pages, out var count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void NativeCleanup_RemovesOnlyIdentifiedFooterAndConservesEveryClinicalPixel()
    {
        using var fixture = new NativePage();
        fixture.Line("Patient name", 15, font: "SourceSansPro");
        fixture.Line("A: Clinical findings with symptoms x", 70);
        fixture.Line("1", 85);
        fixture.Line("month. The original text is retained.", 100);
        fixture.Line("GOALS:", 145);
        fixture.Line("1. Preserve this numbered clinical goal.", 160);
        fixture.Line("Reference to My HealtheVet on VA.gov", 230);
        fixture.Line("Clinical content near footer", 382, size: 3);
        var expected = Ink(fixture.Bitmap);
        fixture.Line("Report generated by My HealtheVet on VA.gov on September 9, 2026Page 440 of 4024",
            390, font: "SourceSansPro", size: 3);
        var page = fixture.Page();
        var original = page.Content.ToArray();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        Assert.Contains(result.Changes, c => c.StartsWith("Removed native Blue Button footer"));
        Assert.Contains(result.Changes, c => c.StartsWith("Joined native prose"));
        using var cleaned = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(expected, Ink(cleaned));
        Assert.True(cleaned.Height < fixture.Bitmap.Height);
        Assert.Equal(original, page.Content.ToArray());
    }

    [Fact]
    public void NativeCleanup_RetainsObjectiveHeadingAndFindingsAsOneFixedRegion()
    {
        using var fixture = new NativePage();
        fixture.Line("O:", 30);
        fixture.Line("GAIT: I with L AFO", 60);
        fixture.Line("Lumbar AROM: WFL", 105);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true);
        Assert.DoesNotContain(result.Changes, c => c.StartsWith("Attached original O:"));
        AssertStructuredGeometry(fixture.Page(), result);
        Assert.Contains(result.Regions, r => r.Kind == "Structured" && r.SourceLines.Contains("O:") &&
            r.SourceLines.Contains("GAIT: I with L AFO"));
        using var cleaned = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(Ink(fixture.Bitmap), Ink(cleaned));
    }

    [Theory]
    [InlineData("1. Clinical findings with symptoms x", 15, 0)]
    [InlineData("Clinical findings with symptoms x", 30, 0)]
    [InlineData("Clinical findings with symptoms x", 15, 24)]
    public void NativeCleanup_DoesNotJoinListsFieldsColumnsParagraphBreaksOrIndentChanges(
        string first, int pitch, int indent)
    {
        using var fixture = new NativePage();
        fixture.Line(first, 40);
        fixture.Line("1", 40 + pitch, x: 20 + indent);
        fixture.Line("month. Original clinical wording remains.", 40 + 2 * pitch);
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true);
        Assert.DoesNotContain(result.Changes, c => c.StartsWith("Joined native prose"));
        using var cleaned = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(Ink(fixture.Bitmap), Ink(cleaned));
    }

    [Fact]
    public void NativeCleanup_ReflowsNarrativeDashListWithHangingIndentWithoutChangingWords()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("-Patient has done well with PAP therapy over the years. Recent struggles", 50, size: 12, x: 45);
        fixture.Line("with", 68, size: 12, x: 57);
        fixture.Line("the mask continue despite otherwise successful treatment.", 86, size: 12, x: 57);
        fixture.Line("- Patient was advised to contact this office if any problems with the", 140, size: 12, x: 45);
        fixture.Line("machine", 158, size: 12, x: 57);
        fixture.Line("or mask occur.", 176, size: 12, x: 57);
        var page = fixture.Page();

        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);

        var prose = result.Regions.Where(r => r.Kind == "Narrative").ToArray();
        Assert.Equal(2, prose.Length);
        Assert.All(prose, region => Assert.Equal(Words(region.SourceLines), Words(region.RenderedLines)));
        Assert.All(prose, region => Assert.True(region.RenderedLines.Count < region.SourceLines.Count));
        AssertAllInk(page, result);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void NativeCleanup_RequiresBlueButtonProvenanceAndUnambiguousGeometry(
        bool blueButton, bool hasGraphics, bool missingGeometry)
    {
        using var fixture = new NativePage();
        fixture.Line("A: Clinical findings with symptoms x", 40);
        fixture.Line("1", 55);
        fixture.Line("month. Original clinical wording remains.", 70);
        var page = fixture.Page(hasGraphics, missingGeometry);
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, blueButton);
        Assert.Empty(result.Changes);
        var crop = VeteransReviewerSourcePageCrop.Crop(page.Content);
        VeteransReviewerSourcePageCropTests.AssertPreserved(page.Content, crop with { Content = result.Content });
    }

    [Fact]
    public void NativeCleanup_RejectsReflowWhenNativeGeometryDoesNotCoverEverySourceMark()
    {
        using var fixture = new NativePage();
        fixture.Line("A: Clinical findings with symptoms x", 40);
        fixture.Line("1", 55);
        fixture.Line("month. Original clinical wording remains.", 70);
        fixture.Bitmap.SetPixel(590, 110, new SKColor(254, 254, 255));
        var result = VeteransReviewerNativeEvidencePage.Prepare(fixture.Page(), true);
        Assert.DoesNotContain(result.Changes, c => c.StartsWith("Joined native prose"));
        using var cleaned = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(Ink(fixture.Bitmap), Ink(cleaned));
    }

    [Fact]
    public void Page21_LocalAttendingLabelDoesNotMakeNeighboringProseStructured()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("Signed: 01/01/2026", 52, size: 12, x: 45);
        fixture.Line("01/01/2026 ADDENDUM                     STATUS: COMPLETED", 88, size: 12, x: 45);
        fixture.Line("Attending: I saw and examined the patient.  I discussed the findings with", 106, size: 12, x: 45);
        fixture.Line("Dr.", 124, size: 12, x: 45);
        fixture.Line("Example and reviewed the note.  I agree wiht the fingings, assessment and", 142, size: 12, x: 45);
        fixture.Line("plan", 160, size: 12, x: 45);
        fixture.Line("as documented.  The patient has been taking the prescribed treatment", 178, size: 12, x: 45);
        fixture.Line("and is", 196, size: 12, x: 45);
        fixture.Line("waiting for the next appointment.", 214, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var prose = Assert.Single(result.Regions.Where(r => r.Kind == "Narrative"));
        Assert.StartsWith("Attending:", prose.RenderedLines[0]);
        Assert.DoesNotContain(prose.RenderedLines, l => l is "Dr." or "plan" or "and is");
        Assert.Equal(Words(prose.SourceLines), Words(prose.RenderedLines));
        Assert.Contains("wiht", Words(prose.RenderedLines));
        Assert.Contains("fingings,", Words(prose.RenderedLines));
        Assert.True(prose.RenderedLines.Count < prose.SourceLines.Count);
        AssertAllInk(page, result);
        AssertStructuredGeometry(page, result);
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("    ")]
    public void RepeatedSpacesAloneDoNotEstablishColumns(string spaces)
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("Attending: The visit was completed." + spaces + "We discussed care with", 50, size: 12, x: 45);
        fixture.Line("Dr.", 68, size: 12, x: 45);
        fixture.Line("Example and confirmed the next steps.", 86, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var prose = Assert.Single(result.Regions);
        Assert.Equal("Narrative", prose.Kind);
        Assert.DoesNotContain(prose.RenderedLines, l => l == "Dr.");
        Assert.Equal(Words(prose.SourceLines), Words(prose.RenderedLines));
        AssertAllInk(page, result);
    }

    [Theory]
    [InlineData("Attending:")]
    [InlineData("Clinician:")]
    public void LocalLabelOnItsOwnLineDoesNotClassifyFollowingProseAsStructured(string label)
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line(label, 32, size: 12, x: 45);
        fixture.Line("The patient described improvement after working with", 50, size: 12, x: 45);
        fixture.Line("Dr.", 68, size: 12, x: 45);
        fixture.Line("Example at the previous appointment.", 86, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var prose = Assert.Single(result.Regions.Where(r => r.Kind == "Narrative"));
        Assert.DoesNotContain(prose.RenderedLines, l => l == "Dr.");
        Assert.Equal(Words(prose.SourceLines), Words(prose.RenderedLines));
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Fact]
    public void RepeatedNativeColumnAnchorsProtectUnlabeledTableGeometry()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("First clinical description", 50, size: 12, x: 45);
        fixture.Line("result cell one", 50, size: 12, x: 350);
        fixture.Line("Second description", 68, size: 12, x: 45);
        fixture.Line("result cell two", 68, size: 12, x: 350);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        Assert.All(result.Regions, r => Assert.Equal("Structured", r.Kind));
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Fact]
    public void Page41_MedicationAndVitalSignRegionsKeepTheirInternalWhitespaceAndXY()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("22) MEDICATION A  TAKE ONE TABLET DAILY                  ACTIVE", 52, size: 12, x: 45);
        fixture.Line("    Indication: EXAMPLE", 70, size: 12, x: 45);
        fixture.Line("23) MEDICATION B  TAKE AS DIRECTED                       ACTIVE", 88, size: 12, x: 45);
        fixture.Line("OBJECTIVE:", 200, size: 12, x: 45);
        fixture.Line("VITAL SIGNS:", 254, size: 12, x: 45);
        fixture.Line("               6/22/26         6/22/26", 290, size: 12, x: 45);
        fixture.Line("                11:24           11:04", 308, size: 12, x: 45);
        fixture.Line("TEMP           ---             97.9 F (36.6 C)", 326, size: 12, x: 45);
        fixture.Line("PULSE          ---             68", 380, size: 12, x: 45);
        fixture.Line("BP             ---             99/67", 398, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var vitals = Assert.Single(result.Regions.Where(r => r.SourceLines.Contains("VITAL SIGNS:")));
        Assert.Contains(vitals.SourceLines, l => l.StartsWith("PULSE"));
        Assert.Contains(vitals.SourceLines, l => l.StartsWith("OBJECTIVE"));
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
        Assert.Contains(result.Changes, c => c.Contains("between regions"));
    }

    [Fact]
    public void DeeplyOutdentedShortNarrativeFragmentsRejoinProseWithoutChangingWordsOrInk()
    {
        using var fixture = new NativePage(1224, 1584);
        var sourceLines = new[]
        {
            "The reviewing clinician discussed the procedure with the Veteran and",
            "and",
            "performed the examination using the documented technique and",
            "correct",
            "vision findings were recorded in the same narrative sequence."
        };
        fixture.Line(sourceLines[0], 70, size: 12, x: 120);
        fixture.Line(sourceLines[1], 88, size: 12, x: 45);
        fixture.Line(sourceLines[2], 106, size: 12, x: 120);
        fixture.Line(sourceLines[3], 124, size: 12, x: 45);
        fixture.Line(sourceLines[4], 142, size: 12, x: 120);
        var page = fixture.Page();

        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);

        var prose = Assert.Single(result.Regions.Where(region => region.Kind == "Narrative"));
        Assert.Equal(Words(sourceLines), Words(prose.RenderedLines));
        Assert.DoesNotContain("and", prose.RenderedLines);
        Assert.DoesNotContain("correct", prose.RenderedLines);
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Fact]
    public void Page42_PhysicalExamAndLabsKeepNativeResultUnitAndReferenceRelationships()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("PHYSICAL EXAM:", 70, size: 12, x: 45);
        fixture.Line("General:         No acute distress", 88, size: 12, x: 45);
        fixture.Line("Skin:            Warm, dry.", 106, size: 12, x: 45);
        fixture.Line("Back:            This field includes a deliberately wrapped clinical", 124, size: 12, x: 45);
        fixture.Line("finding", 142, size: 12, x: 45);
        fixture.Line("that must retain its form geometry.", 160, size: 12, x: 45);
        fixture.Line("LABS:", 250, size: 12, x: 45);
        fixture.Line("Collection DT    Specimen   Test Name      Result   Units     Ref", 286, size: 12, x: 45);
        fixture.Line("Range", 304, size: 12, x: 45);
        fixture.Line("01/01/2026       URINE      SQ-EPI         <1       /HPF      0 -", 322, size: 12, x: 45);
        fixture.Line("50", 340, size: 12, x: 45);
        fixture.Line("                 SERUM     GLUCOSE        95       mg/dL     70-99", 394, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        Assert.All(result.Regions, r => Assert.Equal("Structured", r.Kind));
        Assert.Contains(result.Regions, r => r.SourceLines.Contains("Range") && r.SourceLines.Contains("50"));
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Fact]
    public void Pages56To58_RepairAssessmentProseWithoutMovingHeadingsOrNumberedGoalsWithinRegions()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("RX TODAY:", 70, size: 12, x: 45);
        fixture.Line("1. Evaluation completed", 88, size: 12, x: 45);
        fixture.Line("   -Exercise instructions", 106, size: 12, x: 45);
        fixture.Line("A: The patient was referred for symptoms lasting x", 160, size: 12, x: 45);
        fixture.Line("1", 178, size: 12, x: 45);
        fixture.Line("month. Treatment options reviewed. The patient reports improvement.", 196, size: 12, x: 45);
        fixture.Line("GOALS (3-5 visits):", 250, size: 12, x: 45);
        fixture.Line("1. Continue the first goal", 268, size: 12, x: 45);
        fixture.Line("2. Continue the second goal", 322, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var prose = Assert.Single(result.Regions.Where(r => r.Kind == "Narrative"));
        Assert.Equal(Words(prose.SourceLines), Words(prose.RenderedLines));
        Assert.DoesNotContain(prose.RenderedLines, l => l == "1");
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
    }

    [Theory]
    [InlineData("SCL2 - GI LABS", "No data available for: IRON", "TIBC", "FERRITIN", "C DIFF TOX B GENE PCR")]
    [InlineData("PENDING TESTS", "Tests pending for review: CBC", "TSH", "B12", "VITAMIN D")]
    public void StackedFieldValuesPreserveSourceLinesAndPixelsWhileNearbyProseStillReflows(
        string heading, string first, string second, string third, string fourth)
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line(heading, 52, size: 12, x: 45);
        var fieldLines = new[] { first, second, third, fourth };
        for (var i = 0; i < fieldLines.Length; i++)
            fixture.Line(fieldLines[i], 70 + i * 18, size: 12, x: 45);
        fixture.Line("Attending: We reviewed the findings and discussed care with", 178, size: 12, x: 45);
        fixture.Line("Dr.", 196, size: 12, x: 45);
        fixture.Line("Example before the next appointment.", 214, size: 12, x: 45);
        var page = fixture.Page();
        var result = VeteransReviewerNativeEvidencePage.Prepare(page, true);
        var field = Assert.Single(result.Regions.Where(r => r.SourceLines.Contains(first)));
        Assert.Equal("Structured", field.Kind);
        Assert.Equal(fieldLines, field.SourceLines);
        Assert.Equal(fieldLines, field.RenderedLines);
        AssertStructuredGeometry(page, result);
        AssertAllInk(page, result);
        var prose = Assert.Single(result.Regions.Where(r => r.Kind == "Narrative"));
        Assert.StartsWith("Attending:", prose.SourceLines[0]);
        Assert.True(prose.RenderedLines.Count < prose.SourceLines.Count);
        Assert.DoesNotContain("Dr.", prose.RenderedLines);
        Assert.Equal(Words(prose.SourceLines), Words(prose.RenderedLines));
    }

    private static string[] Words(IEnumerable<string> lines) => lines.SelectMany(l =>
        l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();

    private static void AssertAllInk(PrintableArtifactPage page, VeteransReviewerNativeEvidencePage result)
    {
        using var original = SKBitmap.Decode(page.Content.Span);
        using var rendered = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(Ink(original), Ink(rendered));
    }

    private static void AssertStructuredGeometry(PrintableArtifactPage page, VeteransReviewerNativeEvidencePage result)
    {
        using var source = SKBitmap.Decode(page.Content.Span);
        using var target = SKBitmap.Decode(result.Content.Span);
        var original = source.Pixels; var rendered = target.Pixels;
        foreach (var region in result.Regions.Where(r => r.Kind == "Structured"))
        {
            Assert.Equal(region.SourceBottom - region.SourceTop, region.TargetBottom - region.TargetTop);
            Assert.Equal(region.SourceLines, region.RenderedLines);
            for (var y = region.SourceTop; y < region.SourceBottom; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var tx = x + region.HorizontalOffset;
                var ty = y - region.SourceTop + region.TargetTop;
                if (tx >= 0 && tx < target.Width && ty >= 0 && ty < target.Height)
                    Assert.Equal(original[y * source.Width + x], rendered[ty * target.Width + tx]);
                else Assert.Equal(SKColors.White, original[y * source.Width + x]);
            }
        }
    }

    private static KeyValuePair<uint, int>[] Ink(SKBitmap bitmap) => bitmap.Pixels
        .Where(p => p != SKColors.White).GroupBy(p => (uint)p)
        .Select(g => new KeyValuePair<uint, int>(g.Key, g.Count())).OrderBy(p => p.Key).ToArray();

    internal sealed class NativePage : IDisposable
    {
        public SKBitmap Bitmap { get; }
        private readonly double _width;
        private readonly double _height;
        private readonly List<PrintableArtifactGlyph> _glyphs = [];
        public NativePage(int width = 600, int height = 800, double? pointsWidth = null, double? pointsHeight = null)
        { Bitmap = new(width, height); Bitmap.Erase(SKColors.White); _width = pointsWidth ?? width / 2d; _height = pointsHeight ?? height / 2d; }
        public void Line(string text, double baseline, string font = "RobotoMono", double size = 10, double x = 20)
        {
            foreach (var c in text)
            {
                if (c != ' ')
                {
                    var left = x + .5;
                    var right = x + size * .5;
                    var top = baseline - size * .7;
                    var bottom = baseline + size * .1;
                    _glyphs.Add(new(c.ToString(), x, x + size * .6, top, bottom, left, right, baseline, font, size));
                    var color = new SKColor((byte)(_glyphs.Count % 240), (byte)c, 50);
                    for (var yy = (int)Math.Ceiling(top * Bitmap.Height / _height); yy < (int)(bottom * Bitmap.Height / _height); yy++)
                    for (var xx = (int)Math.Ceiling(left * Bitmap.Width / _width); xx < (int)(right * Bitmap.Width / _width); xx++)
                        if (xx < Bitmap.Width) Bitmap.SetPixel(xx, yy, color);
                }
                x += size * .6;
            }
        }
        public PrintableArtifactPage Page(bool graphics = false, bool missingGeometry = false, int pageNumber = 440)
        {
            using var image = SKImage.FromBitmap(Bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return new() { PageNumber = pageNumber, ContentType = "image/png", Content = png.ToArray(),
                TextGeometry = missingGeometry ? null : new(_width, _height, graphics, _glyphs) };
        }
        public void Dispose() => Bitmap.Dispose();
    }
}
