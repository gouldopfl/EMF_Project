using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class PdfArtifactPrintRenderingProviderTests
{
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

    private static byte[] CreatePdf(int pageCount)
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

