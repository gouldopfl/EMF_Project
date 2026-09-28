using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerNativeEvidencePageTests;

namespace EMF.Tests;

public sealed class VeteransReviewerNativeProseTests
{
    [Theory]
    [InlineData(100, 2020)]
    [InlineData(150, 2023)]
    public void Reconstruct_JoinsMarginFragmentsAcrossPagesWithoutDroppingOrInventingWords(double indent, int pageNumber)
    {
        using var first = new NativePage(1224, 1584);
        first.Line("Result", 20); first.Line("Units", 20, x: 230);
        first.Line("Glucose 100", 35); first.Line("mg/dL", 35, x: 230);
        first.Line("Procedure: Upper GI endoscopy", 50);
        first.Line("- Prior to the procedure, the risks and benefits", 80, x: indent);
        first.Line("and", 95);
        first.Line("alternatives were discussed with the patient.", 110, x: indent);
        first.Line("Patient", 125);
        using var second = new NativePage(1224, 1584);
        second.Line("identification and proposed procedure were verified", 50, x: indent);
        second.Line("by", 65);
        second.Line("the physician and the nurse in the procedure room.", 80, x: indent);
        second.Line("Mental Status Examination: alert and oriented. ASA", 95, x: indent);
        second.Line("Grade", 110);
        second.Line("Assessment: III - A patient with severe systemic disease.", 125, x: indent);
        second.Line("The following parameters were", 155, x: 65);
        second.Line("monitored: heart rate and oxygen saturation.", 170, x: 65);
        second.Line("06/30/2023 10:47 /es/ Example Clinician", 200);
        second.Line("Surname", 215);
        second.Line("ADVANCED", 230);
        second.Line("MSA", 245);
        var pages = new[] { first.Page(pageNumber: pageNumber), second.Page(pageNumber: pageNumber + 1) };
        var original = pages.Select(p => p.Content.ToArray()).ToArray();
        var paragraphs = Assert.IsAssignableFrom<IReadOnlyList<string>>(VeteransReviewerNativeProse.Reconstruct(pages));
        var text = string.Join(" ", paragraphs);
        Assert.Contains("benefits and alternatives", text);
        Assert.Contains(paragraphs, p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^Result {2,}Units$"));
        Assert.Contains(paragraphs, p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^Glucose 100 {2,}mg/dL$"));
        Assert.Contains("Patient identification and proposed procedure were verified by the physician", text);
        Assert.Contains(paragraphs, p => p.Contains("ASA Grade Assessment: III"));
        Assert.Contains(paragraphs, p => p.Contains("parameters were monitored: heart rate"));
        Assert.Contains(paragraphs, p => p.Contains("Example Clinician Surname ADVANCED MSA"));
        Assert.DoesNotContain("MSA", paragraphs);
        Assert.DoesNotContain(paragraphs, p => p is "and" or "Patient" or "by" or "Grade");
        var sourceWords = string.Join(" ", pages.SelectMany(VeteransReviewerNativeProse.Lines).Select(r => r.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(sourceWords, text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        for (var i = 0; i < pages.Length; i++) Assert.Equal(original[i], pages[i].Content.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task MaskScannedSsn_UsesLocalOcrWithoutChangingSource(int rotation)
    {
        using var bitmap = new SkiaSharp.SKBitmap(1200, 180);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);
        using var fontStream = typeof(VeteransReviewerPackageDocxRenderer).Assembly.GetManifestResourceStream(
            "EMF.Extensions.VeteransClaims.Orchestration.Fonts.DejaVuSansMono.ttf")!;
        using var typeface = SkiaSharp.SKTypeface.FromStream(fontStream);
        using var font = new SkiaSharp.SKFont(typeface, 38);
        using var ink = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = true };
        canvas.DrawText("SSN: 123-45-5668", 30, 80, SkiaSharp.SKTextAlign.Left, font, ink);
        canvas.Flush();
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        static byte[] Rotate(byte[] bytes, int rotation)
        {
            if (rotation == 0) return bytes;
            using var input = OpenCvSharp.Cv2.ImDecode(bytes, OpenCvSharp.ImreadModes.Color);
            using var output = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.Rotate(input, output, rotation switch
            { 90 => OpenCvSharp.RotateFlags.Rotate90Clockwise, 180 => OpenCvSharp.RotateFlags.Rotate180, _ => OpenCvSharp.RotateFlags.Rotate90Counterclockwise });
            OpenCvSharp.Cv2.ImEncode(".png", output, out var rotated);
            return rotated;
        }
        var original = Rotate(png.ToArray(), rotation);
        var source = new EMF.Core.Models.PrintableArtifactPage
            { PageNumber = 1, ContentType = "image/png", Content = original };
        var result = VeteransReviewerPagePrivacy.Mask(source);
        var text = await new EMF.Orchestration.Services.PaddleImageOcrService()
            .RecognizeTextAsync(new(Rotate(result.Content.ToArray(), (360 - rotation) % 360)));
        Assert.NotNull(text);
        Assert.DoesNotContain("123-45-5668", text);
        Assert.Contains("5668", text);
        Assert.DoesNotContain("123", text);
        Assert.NotEqual(original, result.Content.ToArray());
        Assert.Equal(original, source.Content.ToArray());
    }

    [Fact]
    public void Reconstruct_RetainsUniquePatientHeaderOnceAcrossContinuationPages()
    {
        using var first = new NativePage(1224, 1584);
        using var second = new NativePage(1224, 1584);
        foreach (var fixture in new[] { first, second })
        {
            fixture.Line("Example, Patient Middle Name Date of birth: January 1, 1950", 20, font: "SourceSansPro");
            fixture.Line("The risks and benefits of this procedure", 80, x: 150);
            fixture.Line("and", 95);
            fixture.Line("alternatives were discussed with the patient.", 110, x: 150);
        }
        var paragraphs = VeteransReviewerNativeProse.Reconstruct([first.Page(pageNumber: 10), second.Page(pageNumber: 11)]);
        Assert.NotNull(paragraphs);
        Assert.Single(paragraphs.Where(p => p.Contains("Patient Middle Name")));
    }

    [Fact]
    public void Reconstruct_LeavesGenuineAlignedFieldsAndTablesNative()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("Result", 50); fixture.Line("Units", 50, x: 200);
        fixture.Line("Glucose", 65); fixture.Line("100 mg/dL", 65, x: 200);
        fixture.Line("Sodium", 80); fixture.Line("140 mmol/L", 80, x: 200);
        Assert.Null(VeteransReviewerNativeProse.Reconstruct([fixture.Page()]));
    }

    [Fact]
    public void MaskNativeSsn_AcceptsMixedCaseImageMimeType()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("SSN: 123-45-5668", 50);
        var original = fixture.Page();
        var source = new EMF.Core.Models.PrintableArtifactPage
        {
            PageNumber = original.PageNumber, ContentType = "IMAGE/PNG",
            Content = original.Content, TextGeometry = original.TextGeometry
        };
        var masked = VeteransReviewerPagePrivacy.Mask(source);
        Assert.Contains("***-**-5668", string.Join(" ", VeteransReviewerNativeProse.Lines(masked).Select(l => l.Text)));
        Assert.Equal(original.Content.ToArray(), source.Content.ToArray());
    }

    [Fact]
    public void MaskNativeSsn_WithLabelOnPreviousLine()
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line("Social Security Number:", 50);
        fixture.Line("123455668", 65);
        var source = fixture.Page();
        var masked = VeteransReviewerPagePrivacy.Mask(source);
        var text = string.Join(" ", VeteransReviewerNativeProse.Lines(masked).Select(l => l.Text));
        Assert.DoesNotContain("123455668", text);
        Assert.Contains("***-**-5668", text);
        Assert.Contains("123455668", string.Join(" ", VeteransReviewerNativeProse.Lines(source).Select(l => l.Text)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaskNativeSsn_PreservesSourcePixelsAndGeometry(bool mrn)
    {
        using var fixture = new NativePage(1224, 1584);
        fixture.Line((mrn ? "MRN: " : "SSN: ") + "123-45-5668", 50);
        fixture.Line("Unchanged clinical findings", 80);
        var page = fixture.Page();
        var original = page.Content.ToArray();
        var originalGlyphs = page.TextGeometry!.Glyphs.ToArray();
        var masked = VeteransReviewerPagePrivacy.Mask(page);
        var text = string.Join(" ", VeteransReviewerNativeProse.Lines(masked).Select(l => l.Text));
        Assert.DoesNotContain("123-45-5668", text);
        Assert.Contains("***-**-5668", text);
        Assert.Contains("Unchanged clinical findings", text);
        Assert.NotEqual(original, masked.Content.ToArray());
        Assert.Equal(original, page.Content.ToArray());
        Assert.Equal(originalGlyphs, page.TextGeometry.Glyphs);
    }
}
