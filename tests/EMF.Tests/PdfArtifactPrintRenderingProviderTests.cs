using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class PdfArtifactPrintRenderingProviderTests
{
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task RenderAsync_IdentifiesSidewaysNativeContentWithoutDoubleRotatingMetadata(int rotation)
    {
        var native = new byte[] { 1, 2, 3 };
        var sideways = new PdfArtifactPrintRenderingProvider(
            new StubContentStore(NativeArticlePdf.CreateSideways(rotation)), new StubPageRenderer(native));
        var page = Assert.Single(await sideways.RenderAsync(new("sideways")));
        Assert.Equal(rotation, page.SuggestedClockwiseRotation);
        Assert.Null(page.TextGeometry);
        Assert.Equal(native, page.Content.ToArray());
        var correctlyRotated = new PdfArtifactPrintRenderingProvider(
            new StubContentStore(NativeArticlePdf.Create((612, 792, rotation))), new StubPageRenderer(native));
        Assert.Equal(0, Assert.Single(await correctlyRotated.RenderAsync(new("rotated"))).SuggestedClockwiseRotation);
    }

    [Fact]
    public async Task RenderAsync_ExposesNativeGlyphGeometryInRasterCoordinates()
    {
        var provider = new PdfArtifactPrintRenderingProvider(
            new StubContentStore(NativeArticlePdf.Create((612, 792, 0))), new StubPageRenderer());
        var page = Assert.Single(await provider.RenderAsync(new("geometry")));
        var geometry = Assert.IsType<EMF.Core.Models.PrintableArtifactTextGeometry>(page.TextGeometry);
        Assert.Equal(612, geometry.Width);
        Assert.Equal(792, geometry.Height);
        Assert.True(geometry.ContainsGraphics);
        Assert.NotEmpty(geometry.Glyphs);
        var first = geometry.Glyphs[0];
        Assert.Equal("S", first.Text);
        Assert.Equal(30, first.X);
        Assert.Equal(35, first.Baseline);
        Assert.True(first.Top < first.Baseline && first.Bottom <= first.Baseline + 5);
        Assert.Contains("Helvetica", first.Font);
    }

    [Fact]
    public async Task RenderRange_RasterizesOnlyRequestedPagesWithOriginalNumbers()
    {
        var images = new StubPageRenderer();
        var provider = new PdfArtifactPrintRenderingProvider(new StubContentStore(CreatePdf(5)), images);
        var pages = await provider.RenderRangeAsync(new("parent"), 3, 4);
        Assert.Equal(new[] { 2, 3 }, images.PageIndexes);
        Assert.Equal(new[] { 3, 4 }, pages.Select(page => page.PageNumber));
    }

    [Fact]
    public async Task RenderRange_RejectsOutOfSourceRangeBeforeRasterizing()
    {
        var images = new StubPageRenderer();
        var provider = new PdfArtifactPrintRenderingProvider(new StubContentStore(CreatePdf(2)), images);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.RenderRangeAsync(new("parent"), 2, 3));
        Assert.Empty(images.PageIndexes);
    }
    [Fact]
    public void CanRender_RecognizesPdf()
    {
        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(null),
                new StubPageRenderer());

        Assert.True(provider.CanRender("application/pdf"));
        Assert.True(provider.CanRender("APPLICATION/PDF"));
        Assert.False(provider.CanRender("text/plain"));
    }

    [Fact]
    public async Task RenderAsync_ReturnsEmptyWhenContentMissing()
    {
        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(null),
                new StubPageRenderer());

        var result =
            await provider.RenderAsync(
                new ArtifactId("artifact-missing"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task RenderAsync_ReturnsOrderedPrintablePages()
    {
        var pdf = CreatePdf(2);
        var renderer = new StubPageRenderer();

        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(pdf),
                renderer);

        var pages =
            await provider.RenderAsync(
                new ArtifactId("artifact-pdf"));

        Assert.Equal(2, pages.Count);

        Assert.Equal(1, pages[0].PageNumber);
        Assert.Equal(2, pages[1].PageNumber);

        Assert.All(
            pages,
            page => Assert.Equal(
                "image/png",
                page.ContentType));

        Assert.Equal(
            new[] { 0, 1 },
            renderer.PageIndexes);
    }

    [Fact]
    public async Task RenderAsync_RejectsOversizedInput()
    {
        var pdf = CreatePdf(1);

        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(pdf),
                new StubPageRenderer(),
                maxInputBytes: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.RenderAsync(
                    new ArtifactId("artifact-pdf")));

        Assert.Equal(
            "PDF print input exceeds the maximum allowed size.",
            ex.Message);
    }

    [Fact]
    public async Task RenderAsync_RejectsExcessPageCount()
    {
        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(CreatePdf(2)),
                new StubPageRenderer(),
                maxPageCount: 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.RenderAsync(
                new ArtifactId("artifact-pdf")));
    }

    [Fact]
    public async Task RenderAsync_RejectsOversizedRenderedPage()
    {
        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(CreatePdf(1)),
                new StubPageRenderer(new byte[2]),
                maxRenderedPageBytes: 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.RenderAsync(
                new ArtifactId("artifact-pdf")));
    }

    [Fact]
    public async Task RenderAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(CreatePdf(1)),
                new StubPageRenderer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.RenderAsync(
                new ArtifactId("artifact-pdf"),
                cancellation.Token));
    }

    [Fact]
    public async Task RenderAsync_OmitsTrulyBlankPages()
    {
        var provider =
            new PdfArtifactPrintRenderingProvider(
                new StubContentStore(CreateBlankPdf(1)),
                new StubPageRenderer());

        var pages =
            await provider.RenderAsync(
                new ArtifactId("artifact-blank-pdf"));

        Assert.Empty(pages);
    }

    private static byte[] CreatePdf(int pageCount)
    {
        using var output = new MemoryStream();

        using (var document =
            SkiaSharp.SKDocument.CreatePdf(output))
        {
            using var paint =
                new SkiaSharp.SKPaint
                {
                    Color = SkiaSharp.SKColors.Black,
                    StrokeWidth = 1,
                    Style = SkiaSharp.SKPaintStyle.Stroke
                };

            for (var i = 0; i < pageCount; i++)
            {
                var canvas = document.BeginPage(72, 72);
                canvas.DrawLine(8, 8, 64, 64, paint);
                document.EndPage();
            }

            document.Close();
        }

        return output.ToArray();
    }

    private static byte[] CreateBlankPdf(int pageCount)
    {
        using var output = new MemoryStream();

        using (var document =
            SkiaSharp.SKDocument.CreatePdf(output))
        {
            for (var i = 0; i < pageCount; i++)
            {
                document.BeginPage(72, 72);
                document.EndPage();
            }

            document.Close();
        }

        return output.ToArray();
    }

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(byte[]? content) =>
            _content = content;

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_content);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPageRenderer :
        IPdfPageImageRenderer
    {
        private readonly byte[] _image;

        public StubPageRenderer(byte[]? image = null) =>
            _image = image ?? [1, 2, 3];

        public List<int> PageIndexes { get; } = [];

        public Task<byte[]> RenderPageAsync(
            ReadOnlyMemory<byte> pdf,
            int pageIndex,
            CancellationToken cancellationToken = default)
        {
            PageIndexes.Add(pageIndex);
            return Task.FromResult(_image);
        }
    }
}
