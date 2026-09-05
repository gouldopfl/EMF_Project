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

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInput()
    {
        var id = new ArtifactId("artifact-text-003");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("12345"));

        var extractor =
            new Utf8ArtifactTextExtractor(
                store,
                maxInputBytes: 4,
                maxExtractedTextChars: 100);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractTextAsync(id));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedExtractedText()
    {
        var id = new ArtifactId("artifact-text-004");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("12345"));

        var extractor =
            new Utf8ArtifactTextExtractor(
                store,
                maxInputBytes: 100,
                maxExtractedTextChars: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractTextAsync(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInputLimit(long value)
    {
        var store = new StubContentStore([]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Utf8ArtifactTextExtractor(
                store,
                maxInputBytes: value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveTextLimit(int value)
    {
        var store = new StubContentStore([]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Utf8ArtifactTextExtractor(
                store,
                maxExtractedTextChars: value));
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
