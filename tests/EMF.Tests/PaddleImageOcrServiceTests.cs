using EMF.Core.Models;
using EMF.Orchestration.Services;
using SkiaSharp;

namespace EMF.Tests;

public sealed class PaddleImageOcrServiceTests
{
    [Fact]
    public async Task RecognizeTextAsync_RecognizesKnownEnglishText()
    {
        using var bitmap =
            new SKBitmap(1600, 600);

        using var canvas =
            new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        using var typeface =
            SKTypeface.FromFile(
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");

        using var font =
            new SKFont(typeface, 56);

        using var paint =
            new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true
            };

        canvas.DrawText(
            "Veteran Evidence Review",
            120,
            180,
            SKTextAlign.Left,
            font,
            paint);

        using var image =
            SKImage.FromBitmap(bitmap);

        using var data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        var service =
            new PaddleImageOcrService();

        var text =
            await service.RecognizeTextAsync(
                new OcrRequest(
                    data.ToArray(),
                    "english"));

        Assert.NotNull(text);

        Assert.Contains(
            "Veteran",
            text,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Evidence",
            text,
            StringComparison.OrdinalIgnoreCase);
    }
}
