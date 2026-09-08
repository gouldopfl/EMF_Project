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
    public const int DefaultDpi = 300;

    private readonly int _dpi;
    private readonly bool _grayscale;

    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;

    private readonly long _maxInputBytes;

    public PdfToImagePageRenderer(
        long maxInputBytes = DefaultMaxInputBytes,
        int dpi = DefaultDpi,
        bool grayscale = true)
    {
        if (maxInputBytes <= 0 ||
            maxInputBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputBytes));
        }

        if (dpi <= 0 || dpi > 1200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpi));
        }

        _maxInputBytes = maxInputBytes;
        _dpi = dpi;
        _grayscale = grayscale;
    }

    public Task<byte[]> RenderPageAsync(
        ReadOnlyMemory<byte> pdf,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        cancellationToken.ThrowIfCancellationRequested();

        if (pdf.Length > _maxInputBytes)
        {
            throw new InvalidDataException(
                "PDF render input exceeds the maximum allowed size.");
        }

        var content = pdf.ToArray();
        var pageSize =
            Conversion.GetPageSize(
                content,
                new Index(pageIndex));

        var pixelWidth =
            Math.Ceiling((double)pageSize.Width * _dpi / 72d);
        var pixelHeight =
            Math.Ceiling((double)pageSize.Height * _dpi / 72d);

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
                Dpi = _dpi,
                Grayscale = _grayscale
            });

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(output.ToArray());
    }
}
