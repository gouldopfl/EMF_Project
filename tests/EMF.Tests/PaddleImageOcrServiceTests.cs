using EMF.Core.Models;
using EMF.Orchestration.Services;
using SkiaSharp;

namespace EMF.Tests;

public sealed class PaddleImageOcrServiceTests
{
    [Fact]
    public async Task RecognizeTextAsync_RejectsOversizedJpegWithSmallExifDimensions()
    {
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();

        using var stream = new MemoryStream();
        stream.Write(jpeg.AsSpan(0, 2));

        // APP1: 44-byte EXIF payload plus the two-byte length field.
        stream.Write(new byte[] { 0xff, 0xe1, 0x00, 0x2e });
        stream.Write("Exif\0\0"u8);

        // Little-endian TIFF with IFD0 width and height both set to 1.
        stream.Write(new byte[]
        {
            0x49, 0x49, 42, 0, 8, 0, 0, 0,
            2, 0,
            0, 1, 4, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            1, 1, 4, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            0, 0, 0, 0
        });

        stream.Write(jpeg.AsSpan(2));

        var service = new PaddleImageOcrService(
            maxPixelCount: 5000);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.RecognizeTextAsync(
                    new OcrRequest(stream.ToArray())));

        Assert.Equal(
            "OCR image dimensions exceed the maximum allowed size.",
            exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInputLimit(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaddleImageOcrService(
                maxInputBytes: value));
    }

    [Fact]
    public void Constructor_RejectsOtherNonPositiveLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaddleImageOcrService(
                maxDimensionPixels: 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaddleImageOcrService(
                maxPixelCount: 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaddleImageOcrService(
                maxExtractedTextChars: 0));
    }


    [Fact]
    public async Task RecognizeTextAsync_RejectsOversizedInput()
    {
        var service =
            new PaddleImageOcrService(
                maxInputBytes: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RecognizeTextAsync(
                new OcrRequest(new byte[5])));
    }


    [Fact]
    public async Task RecognizeTextAsync_RejectsOversizedDimension()
    {
        var image = CreatePng(100, 10);
        var service =
            new PaddleImageOcrService(
                maxDimensionPixels: 50);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RecognizeTextAsync(
                new OcrRequest(image)));
    }

    [Fact]
    public async Task RecognizeTextAsync_RejectsOversizedPixelCount()
    {
        var image = CreatePng(100, 100);
        var service =
            new PaddleImageOcrService(
                maxPixelCount: 5_000);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RecognizeTextAsync(
                new OcrRequest(image)));
    }

    [Fact]
    public async Task RecognizeTextAsync_RejectsOversizedText()
    {
        using var bitmap = new SKBitmap(1600, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var typeface =
            SKTypeface.FromFile(
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");

        using var font = new SKFont(typeface, 56);
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

        using var image = SKImage.FromBitmap(bitmap);
        using var data =
            image.Encode(SKEncodedImageFormat.Png, 100);

        var service =
            new PaddleImageOcrService(
                maxExtractedTextChars: 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RecognizeTextAsync(
                new OcrRequest(
                    data.ToArray(),
                    "english")));
    }


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
    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data =
            image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}
