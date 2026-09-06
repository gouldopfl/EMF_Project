using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class XmlArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsUtf8Text()
    {
        var id = new ArtifactId("xml-1");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("<claim><status>ready</status></claim>"));

        var provider =
            new XmlArtifactTextExtractionProvider(store);

        var text =
            await provider.ExtractTextAsync(id);

        Assert.Equal("<claim><status>ready</status></claim>", text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenMissing()
    {
        var provider =
            new XmlArtifactTextExtractionProvider(
                new StubContentStore(null));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("missing"));

        Assert.Null(text);
    }

    [Fact]
    public void CanExtract_RecognizesPlainText()
    {
        var provider =
            new XmlArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(provider.CanExtract("application/xml"));
        Assert.False(provider.CanExtract("application/pdf"));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsInvalidUtf8()
    {
        var id = new ArtifactId("xml-2");
        var store = new StubContentStore(
            new byte[] { 0xC3, 0x28 });

        var provider =
            new XmlArtifactTextExtractionProvider(store);

        await Assert.ThrowsAsync<DecoderFallbackException>(
            () => provider.ExtractTextAsync(id));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInput()
    {
        var id = new ArtifactId("xml-3");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("12345"));

        var provider =
            new XmlArtifactTextExtractionProvider(
                store,
                maxInputBytes: 4,
                maxExtractedTextChars: 100);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(id));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedExtractedText()
    {
        var id = new ArtifactId("xml-4");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("12345"));

        var provider =
            new XmlArtifactTextExtractionProvider(
                store,
                maxInputBytes: 100,
                maxExtractedTextChars: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInputLimit(long value)
    {
        var store = new StubContentStore([]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new XmlArtifactTextExtractionProvider(
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
            () => new XmlArtifactTextExtractionProvider(
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

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_content);

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
