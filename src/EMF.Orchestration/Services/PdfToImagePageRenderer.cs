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

        using var output = new MemoryStream();

        Conversion.SavePng(
            output,
            pdf.ToArray(),
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
