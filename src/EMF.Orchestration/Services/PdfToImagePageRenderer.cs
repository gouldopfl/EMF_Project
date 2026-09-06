using System.Runtime.Versioning;
using EMF.Core.Contracts;
using PDFtoImage;

namespace EMF.Orchestration.Services;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class PdfToImagePageRenderer :
    IPdfPageImageRenderer
{
    private const int OcrDpi = 300;

    public Task<byte[]> RenderPageAsync(
        ReadOnlyMemory<byte> pdf,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        cancellationToken.ThrowIfCancellationRequested();

        var content = pdf.ToArray();
        var pageSize =
            Conversion.GetPageSize(
                content,
                new Index(pageIndex));

        var pixelWidth =
            Math.Ceiling((double)pageSize.Width * OcrDpi / 72d);
        var pixelHeight =
            Math.Ceiling((double)pageSize.Height * OcrDpi / 72d);

        if (!double.IsFinite(pixelWidth) ||
            !double.IsFinite(pixelHeight) ||
            pixelWidth <= 0 ||
            pixelHeight <= 0 ||
            pixelWidth > PaddleImageOcrService.DefaultMaxDimensionPixels ||
            pixelHeight > PaddleImageOcrService.DefaultMaxDimensionPixels ||
            pixelWidth * pixelHeight >
                PaddleImageOcrService.DefaultMaxPixelCount)
        {
            throw new InvalidDataException(
                "PDF page dimensions exceed the maximum allowed render size.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var output = new MemoryStream();

        Conversion.SavePng(
            output,
            content,
            new Index(pageIndex),
            options: new RenderOptions
            {
                Dpi = OcrDpi,
                Grayscale = true
            });

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(output.ToArray());
    }
}
