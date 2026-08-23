using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EMF.Orchestration.Services;

public sealed class PdfArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private readonly IArtifactContentStore _contentStore;
    private readonly IPdfPageImageRenderer? _pageImageRenderer;
    private readonly IImageOcrService? _ocrService;

    public PdfArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IPdfPageImageRenderer? pageImageRenderer = null,
        IImageOcrService? ocrService = null)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        if ((pageImageRenderer is null) != (ocrService is null))
        {
            throw new ArgumentException(
                "PDF OCR fallback requires both a page renderer and OCR service.");
        }

        _contentStore = contentStore;
        _pageImageRenderer = pageImageRenderer;
        _ocrService = ocrService;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            "application/pdf",
            StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return null;

        using var document =
            PdfDocument.Open(content);

        var builder = new StringBuilder();
        var pageIndex = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text =
                ContentOrderTextExtractor.GetText(page);

            if (string.IsNullOrWhiteSpace(text) &&
                _pageImageRenderer is not null &&
                _ocrService is not null)
            {
                var image =
                    await _pageImageRenderer.RenderPageAsync(
                        content,
                        pageIndex,
                        cancellationToken);

                text =
                    await _ocrService.RecognizeTextAsync(
                        new OcrRequest(image),
                        cancellationToken);
            }

            if (builder.Length > 0)
                builder.AppendLine();

            if (!string.IsNullOrEmpty(text))
                builder.Append(text);

            pageIndex++;
        }

        return builder.ToString();
    }
}
