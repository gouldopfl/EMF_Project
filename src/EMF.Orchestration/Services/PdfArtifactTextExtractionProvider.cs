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
    public const int DefaultMaxPageCount = 10_000;
    public const int DefaultMaxOcrPageCount = 2_000;
    public const long DefaultMaxRenderedPageBytes = 50L * 1024 * 1024;
    public const int DefaultMaxPageTextChars = 2 * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars = 50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly IPdfPageImageRenderer? _pageImageRenderer;
    private readonly IImageOcrService? _ocrService;
    private readonly int _maxPageCount;
    private readonly int _maxOcrPageCount;
    private readonly long _maxRenderedPageBytes;
    private readonly int _maxPageTextChars;
    private readonly int _maxExtractedTextChars;

    public PdfArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IPdfPageImageRenderer? pageImageRenderer = null,
        IImageOcrService? ocrService = null,
        int maxPageCount = DefaultMaxPageCount,
        int maxOcrPageCount = DefaultMaxOcrPageCount,
        long maxRenderedPageBytes = DefaultMaxRenderedPageBytes,
        int maxPageTextChars = DefaultMaxPageTextChars,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        if ((pageImageRenderer is null) != (ocrService is null))
        {
            throw new ArgumentException(
                "PDF OCR fallback requires both a page renderer and OCR service.");
        }

        ValidatePositive(
            maxPageCount,
            nameof(maxPageCount));
        ValidatePositive(
            maxOcrPageCount,
            nameof(maxOcrPageCount));
        ValidatePositive(
            maxRenderedPageBytes,
            nameof(maxRenderedPageBytes));
        ValidatePositive(
            maxPageTextChars,
            nameof(maxPageTextChars));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _pageImageRenderer = pageImageRenderer;
        _ocrService = ocrService;
        _maxPageCount = maxPageCount;
        _maxOcrPageCount = maxOcrPageCount;
        _maxRenderedPageBytes = maxRenderedPageBytes;
        _maxPageTextChars = maxPageTextChars;
        _maxExtractedTextChars = maxExtractedTextChars;
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
        var ocrPageCount = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pageIndex >= _maxPageCount)
            {
                throw new InvalidDataException(
                    "PDF exceeds the maximum allowed page count.");
            }

            var text =
                ContentOrderTextExtractor.GetText(page);

            if (string.IsNullOrWhiteSpace(text) &&
                _pageImageRenderer is not null &&
                _ocrService is not null)
            {
                ocrPageCount++;

                if (ocrPageCount > _maxOcrPageCount)
                {
                    throw new InvalidDataException(
                        "PDF exceeds the maximum allowed OCR page count.");
                }

                var image =
                    await _pageImageRenderer.RenderPageAsync(
                        content,
                        pageIndex,
                        cancellationToken);

                ArgumentNullException.ThrowIfNull(image);

                if (image.LongLength > _maxRenderedPageBytes)
                {
                    throw new InvalidDataException(
                        "PDF rendered page exceeds the maximum allowed image size.");
                }

                text =
                    await _ocrService.RecognizeTextAsync(
                        new OcrRequest(image),
                        cancellationToken);
            }

            if (text?.Length > _maxPageTextChars)
            {
                throw new InvalidDataException(
                    "PDF page text exceeds the maximum allowed size.");
            }

            if (!string.IsNullOrEmpty(text))
            {
                AppendPageText(
                    builder,
                    text);
            }

            pageIndex++;
        }

        return builder.ToString();
    }
    private void AppendPageText(
        StringBuilder builder,
        string text)
    {
        var separatorCharacters =
            builder.Length == 0
                ? 0
                : Environment.NewLine.Length;

        var requiredCharacters =
            text.Length +
            separatorCharacters;

        if (requiredCharacters >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "PDF extracted text exceeds the maximum allowed size.");
        }

        if (builder.Length > 0)
            builder.AppendLine();

        builder.Append(text);
    }

    private static void ValidatePositive(
        long value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "PDF extraction limits must be positive.");
        }
    }

}
