using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class Utf8ArtifactTextExtractorTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsUtf8Text()
    {
        var id = new ArtifactId("artifact-text-001");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("Veteran has chronic instability."));

        var extractor = new Utf8ArtifactTextExtractor(store);

        var text = await extractor.ExtractTextAsync(id);

        Assert.Equal(
            "Veteran has chronic instability.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsInvalidUtf8()
    {
        var id = new ArtifactId("artifact-text-002");
        var store = new StubContentStore(
            new byte[] { 0xC3, 0x28 });

        var extractor = new Utf8ArtifactTextExtractor(store);

        await Assert.ThrowsAsync<DecoderFallbackException>(
            () => extractor.ExtractTextAsync(id));
    }

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(byte[]? content)
        {
            _content = content;
        }

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
