using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using UglyToad.PdfPig;

namespace EMF.Orchestration.Services;

public sealed class PdfArtifactPrintRenderingProvider :
    IArtifactPrintRenderingProvider
{
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;

    public const int DefaultMaxPageCount = 10_000;

    public const long DefaultMaxRenderedPageBytes =
        50L * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly IPdfPageImageRenderer _pageRenderer;
    private readonly long _maxInputBytes;
    private readonly int _maxPageCount;
    private readonly long _maxRenderedPageBytes;

    public PdfArtifactPrintRenderingProvider(
        IArtifactContentStore contentStore,
        IPdfPageImageRenderer pageRenderer,
        long maxInputBytes = DefaultMaxInputBytes,
        int maxPageCount = DefaultMaxPageCount,
        long maxRenderedPageBytes = DefaultMaxRenderedPageBytes)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(pageRenderer);

        if (maxInputBytes <= 0 ||
            maxInputBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputBytes));
        }

        if (maxPageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPageCount));
        }

        if (maxRenderedPageBytes <= 0 ||
            maxRenderedPageBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRenderedPageBytes));
        }

        _contentStore = contentStore;
        _pageRenderer = pageRenderer;
        _maxInputBytes = maxInputBytes;
        _maxPageCount = maxPageCount;
        _maxRenderedPageBytes = maxRenderedPageBytes;
    }

    public bool CanRender(string contentType) =>
        string.Equals(
            contentType,
            "application/pdf",
            StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        if (content.LongLength > _maxInputBytes)
        {
            throw new InvalidDataException(
                "PDF print input exceeds the maximum allowed size.");
        }

        using var document =
            PdfDocument.Open(content);

        var pages =
            new List<PrintableArtifactPage>();

        var pageIndex = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pageIndex >= _maxPageCount)
            {
                throw new InvalidDataException(
                    "PDF exceeds the maximum printable page count.");
            }

            if (page.Letters.Count == 0 &&
                page.NumberOfImages == 0 &&
                page.Paths.Count == 0)
            {
                pageIndex++;
                continue;
            }

            var image =
                await _pageRenderer.RenderPageAsync(
                    content,
                    pageIndex,
                    cancellationToken);

            ArgumentNullException.ThrowIfNull(image);

            if (image.LongLength > _maxRenderedPageBytes)
            {
                throw new InvalidDataException(
                    "PDF rendered page exceeds the maximum printable size.");
            }

            pages.Add(
                new PrintableArtifactPage
                {
                    PageNumber = pageIndex + 1,
                    ContentType = "image/png",
                    Content = image
                });

            pageIndex++;
        }

        return pages;
    }
}
