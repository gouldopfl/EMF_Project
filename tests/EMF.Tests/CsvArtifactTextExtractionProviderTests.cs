using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class CsvArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsUtf8Text()
    {
        var id = new ArtifactId("csv-1");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("name,status\nVeteran,Ready"));

        var provider =
            new CsvArtifactTextExtractionProvider(store);

        var text =
            await provider.ExtractTextAsync(id);

        Assert.Equal("name,status\nVeteran,Ready", text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenMissing()
    {
        var provider =
            new CsvArtifactTextExtractionProvider(
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
            new CsvArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(provider.CanExtract("text/csv"));
        Assert.False(provider.CanExtract("application/pdf"));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsInvalidUtf8()
    {
        var id = new ArtifactId("csv-2");
        var store = new StubContentStore(
            new byte[] { 0xC3, 0x28 });

        var provider =
            new CsvArtifactTextExtractionProvider(store);

        await Assert.ThrowsAsync<DecoderFallbackException>(
            () => provider.ExtractTextAsync(id));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInput()
    {
        var id = new ArtifactId("csv-3");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("12345"));

        var provider =
            new CsvArtifactTextExtractionProvider(
                store,
                maxInputBytes: 4,
                maxExtractedTextChars: 100);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(id));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedExtractedText()
    {
        var id = new ArtifactId("csv-4");
        var store = new StubContentStore(
            Encoding.UTF8.GetBytes("12345"));

        var provider =
            new CsvArtifactTextExtractionProvider(
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
            () => new CsvArtifactTextExtractionProvider(
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
            () => new CsvArtifactTextExtractionProvider(
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
