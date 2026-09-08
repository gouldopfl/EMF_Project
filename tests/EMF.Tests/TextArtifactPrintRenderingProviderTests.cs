using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class TextArtifactPrintRenderingProviderTests
{
    [Fact]
    public void CanRender_RecognizesPlainText()
    {
        var provider = new TextArtifactPrintRenderingProvider(
            new StubContentStore([]));

        Assert.True(provider.CanRender("text/plain"));
        Assert.True(provider.CanRender("TEXT/PLAIN"));
        Assert.True(provider.CanRender("text/csv"));
        Assert.True(provider.CanRender("application/json"));
        Assert.True(provider.CanRender("application/xml"));
        Assert.True(provider.CanRender("text/markdown"));
        Assert.True(provider.CanRender("application/yaml"));
        Assert.False(provider.CanRender("application/pdf"));
    }

    [Fact]
    public async Task RenderAsync_ReturnsPreservedContent()
    {
        byte[] content = [65, 66, 67];

        var provider = new TextArtifactPrintRenderingProvider(
            new StubContentStore(content));

        var pages = await provider.RenderAsync(
            new ArtifactId("artifact-text"));

        var page = Assert.Single(pages);

        Assert.Equal(1, page.PageNumber);
        Assert.Equal("text/plain", page.ContentType);
        Assert.Equal(content, page.Content.ToArray());
    }

    [Fact]
    public async Task RenderAsync_ReturnsEmptyWhenMissing()
    {
        var provider = new TextArtifactPrintRenderingProvider(
            new StubContentStore(null));

        var pages = await provider.RenderAsync(
            new ArtifactId("artifact-missing"));

        Assert.Empty(pages);
    }

    [Fact]
    public async Task RenderAsync_RejectsOversizedInput()
    {
        var provider = new TextArtifactPrintRenderingProvider(
            new StubContentStore([1, 2]),
            maxInputBytes: 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.RenderAsync(
                new ArtifactId("artifact-text")));
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
