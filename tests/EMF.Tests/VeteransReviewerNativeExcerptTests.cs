using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using SkiaSharp;

namespace EMF.Tests;

public sealed class VeteransReviewerNativeExcerptTests
{
    [Fact]
    public void Match_UsesThePreservedExcerptWithoutAddingAdjacentSourceRecords()
    {
        var source = Page(504, "AUDIO HEARING AID CHECK", "ER STAFF ASSESSMENT",
            "Bilateral pes planus", "Signed", "PRIMARY CARE SECURE MESSAGING");
        var result = Assert.Single(VeteransReviewerNativeExcerpt.Match([source],
            "ER STAFF ASSESSMENT\nBilateral pes planus\nSigned"));
        Assert.Equal(new[] { "ER STAFF ASSESSMENT", "Bilateral pes planus", "Signed" },
            result.TextGeometry!.Glyphs.Select(g => g.Text));
        Assert.Throws<InvalidDataException>(() => VeteransReviewerNativeExcerpt.Match(
            [source], "A note that belongs to different source pages"));
    }

    [Fact]
    public void Restrict_RemovesAdjacentBlueButtonRecordsButPreservesEveryIntendedPixel()
    {
        var page = Page(504,
            "PRIMARY CARE SECURE MESSAGING - sleep study",
            "ER STAFF ASSESSMENT", "Bilateral pes planus; low back pain.",
            "Signed: June 22, 2026", "AUDIO HEARING AID CHECK");
        var original = page.Content.ToArray();
        var result = Assert.Single(VeteransReviewerNativeExcerpt.Restrict(
            [page], 504, 504, "ER STAFF ASSESSMENT", "Signed: June 22, 2026"));
        var text = string.Join("\n", result.TextGeometry!.Glyphs.Select(g => g.Text));
        Assert.Contains("Bilateral pes planus", text);
        Assert.DoesNotContain("sleep study", text);
        Assert.DoesNotContain("AUDIO HEARING AID CHECK", text);
        using var before = SKBitmap.Decode(original);
        using var after = SKBitmap.Decode(result.Content.Span);
        for (var y = 0; y < before.Height; y++)
        for (var x = 0; x < before.Width; x++)
            Assert.Equal(y >= 39 && y < 91 ? before.GetPixel(x, y) : SKColors.White, after.GetPixel(x, y));
        Assert.Equal(original, page.Content.ToArray());
        Assert.Equal(504, result.PageNumber);
    }

    [Fact]
    public void Restrict_KeepsInteriorPagesAndUsesOriginalPageCoordinates()
    {
        var middle = Page(506, "Clinical findings");
        var result = VeteransReviewerNativeExcerpt.Restrict(
            [Page(505, "Previous record", "ER STAFF ASSESSMENT"), middle,
             Page(507, "Signed: June 22, 2026", "AUDIO HEARING AID CHECK")],
            505, 507, "ER STAFF ASSESSMENT", "Signed: June 22, 2026");
        Assert.Same(middle, result[1]);
        Assert.Equal(new[] { 505, 506, 507 }, result.Select(p => p.PageNumber));
        var selected = VeteransReviewerPageSelector.Select(result, "506");
        Assert.Same(middle, Assert.Single(selected));
    }

    [Theory]
    [InlineData("Missing ER note", "Signed")]
    [InlineData("ER STAFF ASSESSMENT", "Missing signature")]
    [InlineData("Signed", "ER STAFF ASSESSMENT")]
    [InlineData("", "Signed")]
    public void Restrict_RejectsIncorrectOrReversedLineage(string start, string end) =>
        Assert.Throws<InvalidDataException>(() => VeteransReviewerNativeExcerpt.Restrict(
            [Page(482, "ER STAFF ASSESSMENT", "Clinical findings", "Signed")], 482, 482, start, end));

    [Fact]
    public void Restrict_RejectsAmbiguousBoundaryInsteadOfGuessing() =>
        Assert.Throws<InvalidDataException>(() => VeteransReviewerNativeExcerpt.Restrict(
            [Page(482, "ER STAFF ASSESSMENT", "ER STAFF ASSESSMENT", "Signed")],
            482, 482, "ER STAFF ASSESSMENT", "Signed"));

    [Fact]
    public void Restrict_RejectsMissingBoundaryPageInsteadOfExpanding() =>
        Assert.Throws<InvalidDataException>(() => VeteransReviewerNativeExcerpt.Restrict(
            [Page(483, "ER STAFF ASSESSMENT", "Signed")], 482, 483, "ER STAFF ASSESSMENT", "Signed"));

    internal static PrintableArtifactPage Page(int number, params string[] rows)
    {
        using var bitmap = new SKBitmap(300, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var ink = new SKPaint { Color = SKColors.Black };
        var glyphs = new List<PrintableArtifactGlyph>();
        for (var i = 0; i < rows.Length; i++)
        {
            var top = 20 + i * 20;
            canvas.DrawRect(10, top, 200, 10, ink);
            glyphs.Add(new(rows[i], 10, 210, top, top + 10, 10, 210, top + 10, "Native", 10));
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new PrintableArtifactPage
        {
            PageNumber = number, ContentType = "image/png", Content = data.ToArray(),
            TextGeometry = new(300, 200, false, glyphs)
        };
    }
}
