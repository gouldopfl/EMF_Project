using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using OpenCvSharp;

namespace EMF.Tests;

public sealed class ImageArtifactPrintRenderingProviderTests
{
    [Fact]
    public void CanRender_RecognizesSupportedImages()
    {
        var provider =
            new ImageArtifactPrintRenderingProvider(
                new StubContentStore(null));

        Assert.True(provider.CanRender("image/jpeg"));
        Assert.True(provider.CanRender("image/png"));
        Assert.True(provider.CanRender("image/bmp"));
        Assert.True(provider.CanRender("image/webp"));
        Assert.False(provider.CanRender("image/tiff"));
        Assert.False(provider.CanRender("image/gif"));
    }

    [Fact]
    public async Task RenderAsync_NormalizesImageToPng()
    {
        using var source =
            new Mat(10, 20, MatType.CV_8UC3, Scalar.Red);

        Cv2.ImEncode(".png", source, out var input);

        var provider =
            new ImageArtifactPrintRenderingProvider(
                new StubContentStore(input));

        var pages =
            await provider.RenderAsync(
                new ArtifactId("image-1"));

        var page = Assert.Single(pages);

        Assert.Equal(1, page.PageNumber);
        Assert.Equal("image/png", page.ContentType);

        using var decoded =
            Cv2.ImDecode(
                page.Content.ToArray(),
                ImreadModes.Unchanged);

        Assert.Equal(20, decoded.Width);
        Assert.Equal(10, decoded.Height);
    }

    [Fact]
    public async Task RenderAsync_ReturnsEmptyWhenMissing()
    {
        var provider =
            new ImageArtifactPrintRenderingProvider(
                new StubContentStore(null));

        var pages =
            await provider.RenderAsync(
                new ArtifactId("missing"));

        Assert.Empty(pages);
    }

    private sealed class StubContentStore : IArtifactContentStore
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
}
