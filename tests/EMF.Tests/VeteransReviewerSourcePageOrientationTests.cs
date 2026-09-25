using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using SkiaSharp;

namespace EMF.Tests;

public sealed class VeteransReviewerSourcePageOrientationTests
{
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Orient_PreservesEveryPixelIncludingOddSizedPageEdges(int rotation)
    {
        using var source = new SKBitmap(3, 5);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            source.SetPixel(x, y, new SKColor((byte)(x * 80), (byte)(y * 40), 100));
        using var image = SKImage.FromBitmap(source);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var original = png.ToArray();
        var page = new PrintableArtifactPage { PageNumber = 1, ContentType = "image/png",
            Content = original, SuggestedClockwiseRotation = rotation };
        using var result = SKBitmap.Decode(VeteransReviewerSourcePageOrientation.Orient(page).Span);
        Assert.Equal(rotation == 180 ? 3 : 5, result.Width);
        Assert.Equal(rotation == 180 ? 5 : 3, result.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var (rx, ry) = rotation switch
            {
                90 => (source.Height - 1 - y, x),
                180 => (source.Width - 1 - x, source.Height - 1 - y),
                _ => (y, source.Width - 1 - x)
            };
            Assert.Equal(source.GetPixel(x, y), result.GetPixel(rx, ry));
        }
        Assert.Equal(original, page.Content.ToArray());
    }
}
