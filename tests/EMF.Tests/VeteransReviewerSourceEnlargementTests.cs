using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using SkiaSharp;
using static EMF.Tests.VeteransReviewerNativeEvidencePageTests;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

public sealed class VeteransReviewerSourceEnlargementTests
{
    [Fact]
    public void CaptionedGraphSelectsLosslessRegionAndPreservesCaptionLegendAndFootnote()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Sample graph", 140, x: 50);
        Graph(fixture, 50, 150, 230, 280);
        fixture.Line("Note: sample footnote", 295, x: 50, size: 8);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        var original = page.Content.ToArray();
        var result = Assert.Single(Detect(page).Where(r => r.Selected));
        Assert.Equal("Figure", result.Kind);
        Assert.Equal("caption-and-isolated-raster", result.DetectionMethod);
        Assert.True(result.Bounds.Top <= 133 && result.Bounds.Bottom >= 296);
        Assert.True(result.Magnification >= VeteransReviewerSourceEnlargements.MinimumMagnification);
        AssertPixels(page, result);
        Assert.Equal(original, page.Content.ToArray());
    }

    [Fact]
    public void TwoCaptionedFiguresInSeparateColumnsProduceSeparateNonoverlappingSupplements()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Left", 140, x: 20);
        fixture.Line("Figure 2. Right", 140, x: 340);
        Graph(fixture, 20, 150, 200, 280);
        Graph(fixture, 340, 150, 520, 280);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        var results = Detect(page).Where(r => r.Selected).ToArray();
        Assert.Equal(2, results.Length);
        Assert.False(results[0].Bounds.IntersectsWith(results[1].Bounds));
        Assert.All(results, r => AssertPixels(page, r));
    }

    [Fact]
    public void CaptionedUnruledTableRetainsEveryCellAndFootnote()
    {
        using var fixture = Fixture();
        fixture.Line("Table 1. Sample values", 140, x: 50);
        for (var row = 0; row < 5; row++) fixture.Line($"Group {row}  42  0.8", 155 + row * 14, x: 50);
        fixture.Line("Note: source values", 225, x: 50, size: 8);
        var page = fixture.Page(pageNumber: 1);
        var result = Assert.Single(Detect(page).Where(r => r.Selected));
        Assert.Equal("Table", result.Kind);
        Assert.True(result.Bounds.Bottom >= 226);
        AssertPixels(page, result);
    }

    [Fact]
    public void UncaptionedRuledTableRequiresMultipleIntersectingThinRules()
    {
        using var fixture = Fixture();
        Grid(fixture, 50, 150, 230, 280);
        var page = fixture.Page(graphics: true, missingGeometry: true, pageNumber: 2);
        var result = Assert.Single(Detect(page).Where(r => r.Selected));
        Assert.Equal("Table", result.Kind);
        Assert.Equal("ruled-grid", result.DetectionMethod);
        AssertPixels(page, result);
    }

    [Fact]
    public void FigureTouchingPageMarginClampsPaddingWithoutLosingEdgePixels()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Edge", 140, x: 0);
        Graph(fixture, 0, 150, 180, 280);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        var result = Assert.Single(Detect(page).Where(r => r.Selected));
        Assert.Equal(0, result.Bounds.Left);
        AssertPixels(page, result);
    }

    [Fact]
    public void AmbiguousRasterBlockIsAuditedWithoutCreatingACrop()
    {
        using var fixture = Fixture();
        Graph(fixture, 50, 150, 230, 280);
        var result = Assert.Single(Detect(fixture.Page(graphics: true, pageNumber: 1)));
        Assert.False(result.Selected);
        Assert.Equal("Low", result.Confidence);
        Assert.Equal("Insufficient detection confidence", result.RejectionReason);
        Assert.True(result.Content.IsEmpty);
    }

    [Fact]
    public void EmptyPlotGridCannotQualifyAsAnUncaptionedTable()
    {
        using var fixture = Fixture();
        using (var canvas = new SKCanvas(fixture.Bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = false })
        {
            for (var y = 150; y <= 270; y += 30) canvas.DrawLine(50, y, 230, y, paint);
            for (var x = 50; x <= 230; x += 60) canvas.DrawLine(x, 150, x, 270, paint);
        }
        var result = Assert.Single(Detect(fixture.Page(graphics: true, pageNumber: 1)));
        Assert.False(result.Selected);
        Assert.Equal("Low", result.Confidence);
    }

    [Fact]
    public void WideTableBelowMinimumGainIsDetectedButRejected()
    {
        using var fixture = Fixture();
        Grid(fixture, 10, 100, 600, 680);
        var result = Assert.Single(Detect(fixture.Page(graphics: true, pageNumber: 1)));
        Assert.Equal("High", result.Confidence);
        Assert.False(result.Selected);
        Assert.InRange(result.Magnification, 1, 1.34);
        Assert.Equal("Insufficient enlargement gain", result.RejectionReason);
        Assert.True(result.Content.IsEmpty);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RotatedGridUsesOrientedCoordinatesAndKeepsAllCropPixels(int rotation)
    {
        using var fixture = Fixture();
        Grid(fixture, 50, 150, 230, 280);
        var original = fixture.Page(graphics: true, missingGeometry: true, pageNumber: 1);
        var page = new PrintableArtifactPage { PageNumber = 1, ContentType = "image/png",
            Content = original.Content, SuggestedClockwiseRotation = rotation };
        var oriented = VeteransReviewerSourcePageOrientation.Orient(page);
        using var bitmap = SKBitmap.Decode(oriented.Span);
        var result = Assert.Single(VeteransReviewerSourceEnlargements.Find(page, oriented,
            bitmap.Width * 9000L, bitmap.Height * 9000L).Where(r => r.Selected));
        var mapped = new PrintableArtifactPage { PageNumber = 1, ContentType = "image/png", Content = oriented };
        AssertPixels(mapped, result);
    }

    [Fact]
    public void PreparationFreezesGeometryAndHashesAndReprintReusesTheFrozenDocument()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Sample graph", 140, x: 50);
        Graph(fixture, 50, 150, 230, 280);
        var page = fixture.Page(graphics: true, pageNumber: 3);
        var details = Details([Content(page)]);
        var source = VeteransReviewerPackageSnapshot.Capture(details, []);
        var settings = new VeteransReviewerPackageRenderSettings(new(2026, 9, 30));
        var presentation = VeteransReviewerPackagePresentationPreparation.Prepare(source, settings);
        var frozen = Assert.Single(presentation.FigureTableRegions!.Where(r => r.State == "Frozen"));
        Assert.Equal("publication", frozen.SourceArtifactId);
        Assert.Equal(3, frozen.SourcePage);
        Assert.Equal((612, 792), (frozen.SourceWidth, frozen.SourceHeight));
        Assert.Equal("oriented-raster-pixels/top-left/half-open", frozen.CoordinateSystem);
        Assert.Equal("page-privacy-v1/source-orientation-v1", frozen.MaskingRotationVersion);
        Assert.Equal("Portrait", frozen.Orientation);
        Assert.Equal(0, frozen.ClockwiseRotation);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(page.Content.Span)), frozen.SourceImageSha256);
        Assert.Equal(VeteransReviewerPackageRendererIdentity.Build, frozen.RendererBuild);
        Assert.Null(frozen.RejectionReason);
        var bytes = presentation.MaterializeDocx();
        var persisted = JsonSerializer.Deserialize<ReviewerPackagePresentationSnapshot>(JsonSerializer.Serialize(presentation))!;
        Assert.Equal(bytes, persisted.MaterializeDocx());
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Description + " " + e.Node?.OuterXml)));
        Assert.Equal(2, document.MainDocumentPart!.ImageParts.Count());
        Assert.Contains("Figure enlargement — Source Page 3", document.MainDocumentPart.Document!.Body!.InnerText);
        var hashes = document.MainDocumentPart.ImageParts.Select(part =>
        {
            using var stream = part.GetStream();
            return Convert.ToHexString(SHA256.HashData(stream));
        }).ToArray();
        Assert.Contains(frozen.SourceImageSha256, hashes);
        Assert.Contains(frozen.CropImageSha256, hashes);
        // New live source pixels cannot affect an already prepared presentation.
        fixture.Bitmap.Erase(SKColors.Red);
        Assert.Equal(bytes, persisted.MaterializeDocx());
        Assert.Throws<InvalidDataException>(() => (presentation with { FigureTableRegions =
            new([frozen with { SourceImageSha256 = new string('A', 64) }]) }).ValidateIntegrity());
    }

    [Fact]
    public void FrozenAuditRetainsRejectedCandidatesWithoutRenderingThem()
    {
        using var fixture = Fixture();
        Grid(fixture, 10, 100, 600, 680);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        var presentation = VeteransReviewerPackagePresentationPreparation.Prepare(
            VeteransReviewerPackageSnapshot.Capture(Details([Content(page)]), []), new VeteransReviewerPackageRenderSettings(new(2026, 9, 30)));
        var rejected = Assert.Single(presentation.FigureTableRegions!);
        Assert.Equal("Detected", rejected.State);
        Assert.Equal("Insufficient enlargement gain", rejected.RejectionReason);
        Assert.Null(rejected.CropImageSha256);
        using var document = WordprocessingDocument.Open(new MemoryStream(presentation.MaterializeDocx()), false);
        Assert.Single(document.MainDocumentPart!.ImageParts);
        Assert.DoesNotContain("enlargement —", document.MainDocumentPart.Document!.Body!.InnerText);
    }

    [Fact]
    public void PreparationRejectsAnAuditAppliedToADifferentRasterWithTheSameDimensions()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Sample graph", 140, x: 50);
        Graph(fixture, 50, 150, 230, 280);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        using var stream = new MemoryStream();
        stream.Write(ReviewerPackageTestPreparation.Render(Details([Content(page)])));
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            fixture.Bitmap.Erase(SKColors.Red);
            using var changed = new MemoryStream(fixture.Page(graphics: true, pageNumber: 1).Content.ToArray());
            document.MainDocumentPart!.ImageParts.First().FeedData(changed);
        }
        Assert.Throws<InvalidDataException>(() => VeteransReviewerSourceEnlargements.ReadFrozenAudit(stream.ToArray()));
    }

    [Fact]
    public void HistoricalPresentationWithoutRegionExtensionKeepsItsIntegrityAndReprints()
    {
        using var fixture = Fixture();
        fixture.Line("Ordinary source narrative.", 100, x: 50);
        var source = VeteransReviewerPackageSnapshot.Capture(Details([Content(fixture.Page(pageNumber: 1))]), []);
        var settings = new VeteransReviewerPackageRenderSettings(new(2026, 9, 30));
        var prepared = VeteransReviewerPackagePresentationPreparation.Prepare(source, settings);
        var historical = ReviewerPackagePresentationSnapshot.Create(source, prepared.PackagePreparedDate, prepared.Cover,
            prepared.RenderProfile, prepared.PreparationRendererBuild, prepared.MaterializeDocx());
        var json = JsonSerializer.Serialize(historical);
        Assert.DoesNotContain("FigureTableRegions", json);
        var restored = JsonSerializer.Deserialize<ReviewerPackagePresentationSnapshot>(json)!;
        restored.ValidateIntegrity();
        Assert.Null(restored.FigureTableRegions);
        Assert.Equal(historical.MaterializeDocx(), restored.MaterializeDocx());
    }

    [ReviewerLibreOfficeFact]
    public async Task SupplementPaginationsRetainOriginalAndClearHeadersAndFooters()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Sample graph", 140, x: 50);
        Graph(fixture, 50, 150, 230, 280);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details([Content(page)]), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var imagePages = pdf.GetPages().Where(p => p.NumberOfImages > 0).ToArray();
        Assert.Equal(2, imagePages.Length);
        Assert.Equal(imagePages[0].Number + 1, imagePages[1].Number);
        Assert.Contains("Figure enlargement", imagePages[1].Text);
        Assert.All(imagePages, p =>
        {
            var image = Assert.Single(p.GetImages());
            Assert.InRange(image.BoundingBox.Left, 24, 26);
            Assert.True(image.BoundingBox.Bottom >= 21 && image.BoundingBox.Top <= p.Height - 35);
            Assert.Contains($"Page {p.Number} of {pdf.NumberOfPages}", p.Text);
        });
        if (Environment.GetEnvironmentVariable("EMF_LAYOUT_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "figure-supplement-layout.docx"), output.Docx!);
            await File.WriteAllBytesAsync(Path.Combine(directory, "figure-supplement-layout.pdf"), output.Pdf!);
        }
    }

    [Theory]
    [InlineData(180, 300, false)]
    [InlineData(400, 120, true)]
    public void OrientationChoosesLargestActualContentBoxScale(int width, int height, bool landscape)
    {
        var region = new SKRectI(0, 0, width, height);
        var portrait = VeteransReviewerSourceEnlargements.ContentBox(false);
        var wide = VeteransReviewerSourceEnlargements.ContentBox(true);
        var fit = VeteransReviewerSourceEnlargements.BestFit(region);
        var portraitScale = Math.Min(portrait.Width / (double)width, portrait.Height / (double)height);
        var landscapeScale = Math.Min(wide.Width / (double)width, wide.Height / (double)height);
        Assert.Equal(landscape, fit.Landscape);
        Assert.Equal(Math.Max(portraitScale, landscapeScale), fit.Scale);
    }

    [Fact]
    public void EqualOrientationScalesDeterministicallyChoosePortrait()
    {
        var box = VeteransReviewerSourceEnlargements.ContentBox(true);
        var fit = VeteransReviewerSourceEnlargements.BestFit(new SKRectI(0, 0, 1404, 1351));
        Assert.False(fit.Landscape);
        Assert.Equal(box.Height / 1351d, fit.Scale);
    }

    [Fact]
    public void GainUsesActualSourceDisplayAndThresholdAppliesAfterOrientationChoice()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Wide graph", 140, x: 50);
        Graph(fixture, 50, 150, 550, 280);
        var page = fixture.Page(graphics: true, pageNumber: 1);
        var box = VeteransReviewerSourceEnlargements.ContentBox(false);
        var first = Assert.Single(VeteransReviewerSourceEnlargements.Find(page, page.Content,
            612 * 9000L, 792 * 9000L));
        Assert.True(first.Selected);
        Assert.True(first.Landscape);
        var largerSource = Assert.Single(VeteransReviewerSourceEnlargements.Find(page, page.Content,
            612 * 18000L, 792 * 18000L));
        Assert.Equal(first.Magnification / 2, largerSource.Magnification, 10);
        Assert.False(largerSource.Selected);
        Assert.Equal("Insufficient enlargement gain", largerSource.RejectionReason);
    }

    [Fact]
    public void NewPresentationRedetectsWhileReprintUsesItsFrozenSelections()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Graph", 140, x: 50);
        Graph(fixture, 50, 150, 230, 280);
        var settings = new VeteransReviewerPackageRenderSettings(new(2026, 9, 30));
        var first = VeteransReviewerPackagePresentationPreparation.Prepare(
            VeteransReviewerPackageSnapshot.Capture(Details([Content(fixture.Page(graphics: true, pageNumber: 1))]), []), settings);
        var bytes = first.MaterializeDocx();
        fixture.Bitmap.Erase(SKColors.White);
        Graph(fixture, 50, 150, 230, 280);
        var fresh = VeteransReviewerPackagePresentationPreparation.Prepare(
            VeteransReviewerPackageSnapshot.Capture(Details([Content(fixture.Page(graphics: true, missingGeometry: true, pageNumber: 1))]), []), settings);
        Assert.Equal(bytes, first.MaterializeDocx());
        Assert.Contains(first.FigureTableRegions!, r => r.State == "Frozen");
        Assert.DoesNotContain(fresh.FigureTableRegions!, r => r.State == "Frozen");
        Assert.Contains(fresh.FigureTableRegions!, r => r.RejectionReason == "Insufficient detection confidence");
    }

    [ReviewerLibreOfficeFact]
    public async Task MultipleSupplementsUseFullBoxesAndKeepNumberingAcrossOrientationChanges()
    {
        using var fixture = Fixture();
        fixture.Line("Figure 1. Wide graph", 100, x: 50);
        Graph(fixture, 50, 110, 550, 220);
        fixture.Line("Figure 2. Tall graph", 350, x: 50);
        Graph(fixture, 50, 360, 200, 650);
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details([Content(fixture.Page(graphics: true, pageNumber: 1))]), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().Where(p => p.NumberOfImages > 0).ToArray();
        Assert.Equal(3, pages.Length);
        Assert.True(pages[0].Height > pages[0].Width);
        Assert.True(pages[1].Width > pages[1].Height);
        Assert.True(pages[2].Height > pages[2].Width);
        Assert.Equal(new[] { pages[0].Number, pages[0].Number + 1, pages[0].Number + 2 }, pages.Select(p => p.Number));
        Assert.DoesNotContain("enlargement", pages[0].Text);
        Assert.All(pages, p =>
        {
            Assert.Contains("CONFIDENTIAL", p.Text);
            Assert.Contains($"Page {p.Number} of {pdf.NumberOfPages}", p.Text);
            var image = Assert.Single(p.GetImages());
            Assert.InRange(image.BoundingBox.Left, 24, 26);
            Assert.True(image.BoundingBox.Top <= p.Height - 35);
            Assert.True(image.BoundingBox.Bottom >= 21);
        });
        foreach (var p in pages.Skip(1))
        {
            var image = Assert.Single(p.GetImages());
            // At least one limiting dimension fills the available content box.
            Assert.True(Math.Abs(image.BoundingBox.Width - (p.Width - 50.4)) < 2 ||
                Math.Abs(image.BoundingBox.Height - (p.Height - 71.6)) < 2);
        }
        if (Environment.GetEnvironmentVariable("EMF_LAYOUT_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "orientation-supplements-layout.pdf"), output.Pdf!);
            await File.WriteAllBytesAsync(Path.Combine(directory, "orientation-supplements-layout.docx"), output.Docx!);
        }
    }

    private static NativePage Fixture() => new(612, 792, 612, 792);
    private static IReadOnlyList<VeteransReviewerSourceEnlargement> Detect(PrintableArtifactPage page) =>
        VeteransReviewerSourceEnlargements.Find(page, page.Content, 612 * 9000L, 792 * 9000L);
    private static VeteransReviewerArtifactContent Content(PrintableArtifactPage page) => new()
    {
        Artifact = new() { Id = new("publication"), Name = "Synthetic publication", ArtifactType = "file" },
        SourceName = "Synthetic journal", Text = "", PrintablePages = [page],
        Appendix = VeteransReviewerPackageAppendix.MedicalLiterature,
        ReviewedMedicalLiteratureClassifications = [new()
        {
            ArtifactId = new("publication"), Association = new()
            {
                RequirementId = new("medical-nexus"), MedicalLiteratureSourceId = new("synthetic-publication"),
                GuidanceRole = "Supporting", Description = "Synthetic reviewed relevance."
            },
            PromotedBy = "test", PromotedUtc = DateTimeOffset.UnixEpoch, ReviewedBy = "test",
            ReviewedUtc = DateTimeOffset.UnixEpoch, IntelligenceOutput = "Reviewed synthetic publication.",
            CapabilityId = "test", ProviderId = "test", CorrelationId = "test", EngineName = "test",
            StartedUtc = DateTimeOffset.UnixEpoch, CompletedUtc = DateTimeOffset.UnixEpoch,
            RequiresReview = false, Warnings = [], SourceExcerpts = []
        }]
    };
    private static void Graph(NativePage fixture, int left, int top, int right, int bottom)
    {
        using var canvas = new SKCanvas(fixture.Bitmap);
        using var paint = new SKPaint { Color = new SKColor(50, 100, 180), IsAntialias = false };
        canvas.DrawRect(new SKRect(left, top, right, bottom), paint);
    }
    private static void Grid(NativePage fixture, int left, int top, int right, int bottom)
    {
        using var canvas = new SKCanvas(fixture.Bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = false };
        for (var row = 0; row <= 4; row++)
            canvas.DrawLine(left, top + (bottom - top) * row / 4, right, top + (bottom - top) * row / 4, paint);
        for (var column = 0; column <= 3; column++)
            canvas.DrawLine(left + (right - left) * column / 3, top, left + (right - left) * column / 3, bottom, paint);
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 3; column++)
        {
            var x = left + (right - left) * column / 3 + 8;
            var y = top + (bottom - top) * row / 4 + 8;
            canvas.DrawRect(new SKRect(x, y, x + 3, y + 5), paint);
            canvas.DrawRect(new SKRect(x + 8, y, x + 11, y + 5), paint);
        }
    }
    private static void AssertPixels(PrintableArtifactPage page, VeteransReviewerSourceEnlargement result)
    {
        using var original = SKBitmap.Decode(page.Content.Span);
        using var crop = SKBitmap.Decode(result.Content.Span);
        Assert.Equal(result.Bounds.Width, crop.Width);
        Assert.Equal(result.Bounds.Height, crop.Height);
        for (var y = 0; y < crop.Height; y++)
        for (var x = 0; x < crop.Width; x++)
            Assert.Equal(original.GetPixel(result.Bounds.Left + x, result.Bounds.Top + y), crop.GetPixel(x, y));
    }
}
