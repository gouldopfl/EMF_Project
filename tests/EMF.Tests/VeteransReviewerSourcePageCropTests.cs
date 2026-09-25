using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using SkiaSharp;

namespace EMF.Tests;

public sealed class VeteransReviewerSourcePageCropTests
{
    [Fact]
    public void Crop_RetainsFaintMarksPublisherFurnitureAndSafetyMargin()
    {
        using var source = WhitePage(400, 600);
        // Header, footer, DOI link, page number, caption and distant figure.
        source.SetPixel(70, 30, SKColors.Black);
        source.SetPixel(65, 560, new SKColor(254, 254, 254));
        source.SetPixel(40, 500, SKColors.Blue);
        source.SetPixel(370, 550, SKColors.Black);
        source.SetPixel(45, 200, new SKColor(255, 254, 255));
        source.SetPixel(350, 80, SKColors.Red);
        var original = Encode(source);
        var crop = VeteransReviewerSourcePageCrop.Crop(original);
        Assert.Equal((36, 26, 339, 539), (crop.Left, crop.Top, crop.Width, crop.Height));
        AssertPreserved(original, crop);
        Assert.Equal(original, Encode(source));
    }

    [Fact]
    public void Crop_AsymmetricOuterMarginsBecomeEqualSafetyPadding()
    {
        using var source = WhitePage(600, 800);
        // Large blank left margin, narrow right margin: translate the complete
        // preserved region without changing any relative source positions.
        source.SetPixel(220, 80, SKColors.Black);
        source.SetPixel(500, 720, SKColors.Blue);
        source.SetPixel(350, 300, new SKColor(254, 254, 254));
        var original = Encode(source);
        var crop = VeteransReviewerSourcePageCrop.Crop(original);
        Assert.Equal((214, 74, 293, 653), (crop.Left, crop.Top, crop.Width, crop.Height));
        Assert.Equal(6, 220 - crop.Left);
        Assert.Equal(6, crop.Left + crop.Width - 1 - 500);
        AssertPreserved(original, crop);
    }

    [Fact]
    public void Crop_Page85Source15LayoutDoesNotMistakeInternalReferenceIndentForOuterMargin()
    {
        using var source = WhitePage(612, 792);
        // Source page 15's native text bounds: publisher furniture begins at
        // x=35pt; references begin at x=200.5pt; both extend to x=576pt.
        // All-page ink bounds must include the furniture, not only references.
        source.SetPixel(35, 36, SKColors.Black); // PLOS ONE
        source.SetPixel(576, 47, SKColors.Black); // running title
        for (var x = 35; x <= 576; x++) source.SetPixel(x, 55, SKColors.Black);
        source.SetPixel(200, 78, SKColors.Black); // reference number 17
        source.SetPixel(576, 698, SKColors.Blue); // final reference DOI
        source.SetPixel(35, 757, SKColors.Blue); // publisher footer/DOI
        source.SetPixel(576, 757, SKColors.Black); // original page number
        var original = Encode(source);
        var crop = VeteransReviewerSourcePageCrop.Crop(original);
        Assert.Equal((28, 29, 556, 736), (crop.Left, crop.Top, crop.Width, crop.Height));
        AssertPreserved(original, crop);
        using var result = SKBitmap.Decode(crop.Content.Span);
        Assert.Equal(SKColors.Black, result.GetPixel(7, 7));
        Assert.Equal(SKColors.Blue, result.GetPixel(7, 728));
        // Cropping may not shift references relative to publisher content.
        Assert.Equal(SKColors.Black, result.GetPixel(172, 49));
        Assert.Equal(165, (200 - crop.Left) - (35 - crop.Left));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crop_LeavesBlankOrEdgeToEdgePagesUnchanged(bool edgeMarks)
    {
        using var source = WhitePage(100, 140);
        if (edgeMarks)
        {
            source.SetPixel(0, 0, SKColors.Black);
            source.SetPixel(99, 139, new SKColor(254, 255, 255));
        }
        var original = Encode(source);
        var crop = VeteransReviewerSourcePageCrop.Crop(original);
        Assert.Equal(original, crop.Content.ToArray());
        AssertPreserved(original, crop);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Crop_AfterOrientationPreservesAllSourcePixelsAndResolution(int rotation)
    {
        using var source = WhitePage(101, 151);
        for (var y = 25; y < 120; y++)
        for (var x = 15; x < 80; x++)
            source.SetPixel(x, y, new SKColor((byte)x, (byte)y, 210));
        var original = Encode(source);
        var page = new PrintableArtifactPage { PageNumber = 7, ContentType = "image/png",
            Content = original, SuggestedClockwiseRotation = rotation };
        var oriented = VeteransReviewerSourcePageOrientation.Orient(page);
        var crop = VeteransReviewerSourcePageCrop.Crop(oriented);
        Assert.Equal(rotation is 90 or 270 ? 99 : 69, crop.Width);
        Assert.Equal(rotation is 90 or 270 ? 69 : 99, crop.Height);
        AssertPreserved(oriented, crop);
        Assert.Equal(original, page.Content.ToArray());
    }

    internal static void AssertPreserved(ReadOnlyMemory<byte> original, VeteransReviewerSourcePageCrop crop)
    {
        using var source = SKBitmap.Decode(original.Span);
        using var result = SKBitmap.Decode(crop.Content.Span);
        Assert.Equal(crop.Width, result.Width);
        Assert.Equal(crop.Height, result.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var inside = x >= crop.Left && x < crop.Left + crop.Width &&
                y >= crop.Top && y < crop.Top + crop.Height;
            if (inside)
                Assert.Equal(source.GetPixel(x, y), result.GetPixel(x - crop.Left, y - crop.Top));
            else
                Assert.Equal(SKColors.White, source.GetPixel(x, y));
        }
    }

    private static SKBitmap WhitePage(int width, int height)
    {
        var source = new SKBitmap(width, height);
        source.Erase(SKColors.White);
        return source;
    }

    private static byte[] Encode(SKBitmap source)
    {
        using var image = SKImage.FromBitmap(source);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
