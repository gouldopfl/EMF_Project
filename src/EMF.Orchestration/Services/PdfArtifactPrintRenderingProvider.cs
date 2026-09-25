using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EMF.Orchestration.Services;

public sealed class PdfArtifactPrintRenderingProvider :
    IArtifactPrintRenderingProvider, IArtifactPageRangePrintRenderer
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

    public Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default) =>
        RenderCoreAsync(artifactId, 1, null, cancellationToken);

    public Task<IReadOnlyList<PrintableArtifactPage>> RenderRangeAsync(
        ArtifactId artifactId, int startPage, int endPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPage, startPage);
        return RenderCoreAsync(artifactId, startPage, endPage, cancellationToken);
    }

    private async Task<IReadOnlyList<PrintableArtifactPage>> RenderCoreAsync(
        ArtifactId artifactId, int startPage, int? endPage,
        CancellationToken cancellationToken)
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

        if (document.NumberOfPages > _maxPageCount)
            throw new InvalidDataException("PDF exceeds the maximum printable page count.");
        var lastPage = endPage ?? document.NumberOfPages;
        if (endPage is not null && (startPage > document.NumberOfPages || lastPage > document.NumberOfPages))
            throw new InvalidDataException("Requested printable source range exceeds the PDF page count.");

        for (var pageIndex = startPage - 1; pageIndex < lastPage; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageIndex + 1);

            if (page.Letters.Count == 0 &&
                page.NumberOfImages == 0 &&
                page.Paths.Count == 0)
            {
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
                    Content = image,
                    SuggestedClockwiseRotation = SuggestedRotation(page),
                    TextGeometry = TextGeometry(page)
                });

        }

        return pages;
    }

    private static int SuggestedRotation(Page page)
    {
        // A dominant native baseline direction can identify a sideways table
        // even when /Rotate is zero. Sparse labels or mixed directions cannot.
        var letters = page.Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).ToArray();
        if (letters.Length < 20) return 0;
        var dominant = letters.GroupBy(letter => letter.TextOrientation)
            .OrderByDescending(group => group.Count()).First();
        if (dominant.Count() < letters.Length * 0.8) return 0;
        return dominant.Key switch
        {
            TextOrientation.Rotate270 => 90,
            TextOrientation.Rotate90 => 270,
            TextOrientation.Rotate180 => 180,
            _ => 0
        };
    }

    private static PrintableArtifactTextGeometry? TextGeometry(Page page)
    {
        // These coordinates are used only when their mapping to the raster is
        // unambiguous. Rotated/cropped coordinate systems retain native images.
        if (page.Rotation.Value != 0 || page.CropBox.Bounds.Left != 0 || page.CropBox.Bounds.Bottom != 0 ||
            page.Letters.Any(l => l.TextOrientation != TextOrientation.Horizontal)) return null;
        return new(page.Width, page.Height, page.NumberOfImages > 0 || page.Paths.Count > 0,
            page.Letters.Select(l => new PrintableArtifactGlyph(l.Value,
                l.StartBaseLine.X, l.EndBaseLine.X, page.Height - l.BoundingBox.Top,
                page.Height - l.BoundingBox.Bottom, l.BoundingBox.Left, l.BoundingBox.Right,
                page.Height - l.StartBaseLine.Y, l.FontName ?? string.Empty, l.FontSize)).ToArray());
    }
}
