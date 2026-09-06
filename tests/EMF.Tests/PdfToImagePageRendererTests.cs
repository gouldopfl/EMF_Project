using System.Runtime.Versioning;
using EMF.Orchestration.Services;

namespace EMF.Tests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class PdfToImagePageRendererTests
{
    [Theory]
    [InlineData(4801, 72)]
    [InlineData(72, 4801)]
    [InlineData(1600, 1600)]
    public async Task RenderPageAsync_RejectsOversizedPage(
        int widthPoints,
        int heightPoints)
    {
        using var output = new MemoryStream();

        using (var document = SkiaSharp.SKDocument.CreatePdf(output))
        {
            var canvas =
                document.BeginPage(widthPoints, heightPoints);

            using var paint =
                new SkiaSharp.SKPaint
                {
                    Color = SkiaSharp.SKColors.Black
                };

            canvas.DrawRect(1, 1, 10, 10, paint);
            document.EndPage();
            document.Close();
        }

        var renderer = new PdfToImagePageRenderer();

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => renderer.RenderPageAsync(output.ToArray(), 0));

        Assert.Equal(
            "PDF page dimensions exceed the maximum allowed render size.",
            exception.Message);
    }

    [Fact]
    public async Task RenderPageAsync_RendersPdfPageAsPng()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.pdf");

        var pdf =
            await File.ReadAllBytesAsync(path);

        var renderer =
            new PdfToImagePageRenderer();

        var image =
            await renderer.RenderPageAsync(
                pdf,
                0);

        Assert.NotEmpty(image);

        Assert.Equal(
            new byte[]
            {
                0x89,
                0x50,
                0x4E,
                0x47,
                0x0D,
                0x0A,
                0x1A,
                0x0A
            },
            image[..8]);
    }

    [Fact]
    public async Task RenderPageAsync_RejectsNegativePageIndex()
    {
        var renderer =
            new PdfToImagePageRenderer();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderPageAsync(
                Array.Empty<byte>(),
                -1));
    }

    [Fact]
    public async Task RenderPageAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var renderer =
            new PdfToImagePageRenderer();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderPageAsync(
                Array.Empty<byte>(),
                0,
                cancellation.Token));
    }
}
