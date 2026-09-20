using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace EMF.Orchestration.Services;

public sealed class PdfArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxPageCount = 10_000;
    public const int DefaultMaxOcrPageCount = 2_000;
    public const long DefaultMaxRenderedPageBytes = 50L * 1024 * 1024;
    public const int DefaultMaxPageTextChars = 2 * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars = 50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly IPdfPageImageRenderer? _pageImageRenderer;
    private readonly IImageOcrService? _ocrService;
    private readonly long _maxInputBytes;
    private readonly int _maxPageCount;
    private readonly int _maxOcrPageCount;
    private readonly long _maxRenderedPageBytes;
    private readonly int _maxPageTextChars;
    private readonly int _maxExtractedTextChars;

    public PdfArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IPdfPageImageRenderer? pageImageRenderer = null,
        IImageOcrService? ocrService = null,
        long maxInputBytes = DefaultMaxInputBytes,
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
            maxInputBytes,
            nameof(maxInputBytes));
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
        _maxInputBytes = maxInputBytes;
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

    public Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default) =>
        ExtractTextCoreAsync(
            artifactId,
            null,
            null,
            cancellationToken);

    public Task<string?> ExtractPageRangeTextAsync(
        ArtifactId artifactId,
        int startPage,
        int endPage,
        CancellationToken cancellationToken = default)
    {
        if (startPage <= 0 || endPage < startPage)
            throw new ArgumentOutOfRangeException(nameof(startPage));

        if ((long)endPage - startPage + 1 > _maxPageCount)
            throw new InvalidDataException(
                "PDF page range exceeds the maximum allowed page count.");

        return ExtractTextCoreAsync(
            artifactId,
            startPage,
            endPage,
            cancellationToken);
    }

    public Task<IReadOnlyList<ExtractedArtifactTextPage>?>
        ExtractPagesAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        ExtractPagesCoreAsync(
            artifactId,
            null,
            null,
            cancellationToken);

    public Task<IReadOnlyList<ExtractedArtifactTextPage>?>
        ExtractPageRangePagesAsync(
            ArtifactId artifactId,
            int startPage,
            int endPage,
            CancellationToken cancellationToken = default)
    {
        if (startPage <= 0 || endPage < startPage)
            throw new ArgumentOutOfRangeException(nameof(startPage));

        if ((long)endPage - startPage + 1 > _maxPageCount)
            throw new InvalidDataException(
                "PDF page range exceeds the maximum allowed page count.");

        return ExtractPagesCoreAsync(
            artifactId,
            startPage,
            endPage,
            cancellationToken);
    }

    private async Task<string?> ExtractTextCoreAsync(
        ArtifactId artifactId,
        int? startPage,
        int? endPage,
        CancellationToken cancellationToken)
    {
        var pages =
            await ExtractPagesCoreAsync(
                artifactId,
                startPage,
                endPage,
                cancellationToken);

        if (pages is null)
            return null;

        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            if (!string.IsNullOrEmpty(page.Text))
                AppendPageText(builder, page.Text);
        }

        return builder.ToString();
    }

    private async Task<IReadOnlyList<ExtractedArtifactTextPage>?>
        ExtractPagesCoreAsync(
            ArtifactId artifactId,
            int? startPage,
            int? endPage,
            CancellationToken cancellationToken)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        if (content.LongLength > _maxInputBytes)
            throw new InvalidDataException(
                "PDF input exceeds the maximum allowed size.");

        using var document =
            PdfDocument.Open(content);

        var pages =
            new List<ExtractedArtifactTextPage>();

        var pageIndex = 0;
        var ocrPageCount = 0;
        var selectedPageCount = 0;
        var extractedTextCharacters = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pageIndex >= _maxPageCount)
                throw new InvalidDataException(
                    "PDF exceeds the maximum allowed page count.");

            var pageNumber = pageIndex + 1;

            if (startPage.HasValue &&
                pageNumber < startPage.Value)
            {
                pageIndex++;
                continue;
            }

            if (endPage.HasValue &&
                pageNumber > endPage.Value)
            {
                break;
            }

            selectedPageCount++;

            var text =
                ExtractReadingOrderText(page);

            if (string.IsNullOrWhiteSpace(text) &&
                _pageImageRenderer is not null &&
                _ocrService is not null)
            {
                ocrPageCount++;

                if (ocrPageCount > _maxOcrPageCount)
                    throw new InvalidDataException(
                        "PDF exceeds the maximum allowed OCR page count.");

                var image =
                    await _pageImageRenderer.RenderPageAsync(
                        content,
                        pageIndex,
                        cancellationToken);

                ArgumentNullException.ThrowIfNull(image);

                if (image.LongLength > _maxRenderedPageBytes)
                    throw new InvalidDataException(
                        "PDF rendered page exceeds the maximum allowed image size.");

                text =
                    await _ocrService.RecognizeTextAsync(
                        new OcrRequest(image),
                        cancellationToken);
            }

            if (text?.Length > _maxPageTextChars)
                throw new InvalidDataException(
                    "PDF page text exceeds the maximum allowed size.");

            text ??= string.Empty;

            if (text.Length > 0)
            {
                var separatorCharacters =
                    extractedTextCharacters == 0
                        ? 0
                        : Environment.NewLine.Length;

                var requiredCharacters =
                    text.Length +
                    separatorCharacters;

                if (requiredCharacters >
                    _maxExtractedTextChars -
                    extractedTextCharacters)
                {
                    throw new InvalidDataException(
                        "PDF extracted text exceeds the maximum allowed size.");
                }

                extractedTextCharacters += requiredCharacters;
            }

            pages.Add(
                new ExtractedArtifactTextPage
                {
                    PageNumber = pageNumber,
                    Text = text
                });

            pageIndex++;
        }

        if (startPage.HasValue &&
            selectedPageCount !=
                endPage!.Value - startPage.Value + 1)
        {
            throw new InvalidDataException(
                "PDF page range exceeds the available page count.");
        }

        return pages;
    }


    private static string ExtractReadingOrderText(
        UglyToad.PdfPig.Content.Page page)
    {
        var words =
            NearestNeighbourWordExtractor.Instance
                .GetWords(page.Letters);

        var blocks =
            RecursiveXYCut.Instance
                .GetBlocks(words);

        if (blocks.Count <= 1)
        {
            blocks =
                DocstrumBoundingBoxes.Instance
                    .GetBlocks(words);
        }

        var orderedBlocks =
            new UnsupervisedReadingOrderDetector(
                    useRenderingOrder: false)
                .Get(blocks)
                .ToArray();

        if (orderedBlocks.Length == 0)
            return ContentOrderTextExtractor.GetText(page);

        var builder = new StringBuilder();

        foreach (var block in orderedBlocks)
        {
            var blockText =
                block.Text.Normalize(
                    NormalizationForm.FormKC);

            if (string.IsNullOrWhiteSpace(blockText))
                continue;

            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(blockText.Trim());
        }

        return builder.Length == 0
            ? ContentOrderTextExtractor.GetText(page)
            : builder.ToString();
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
