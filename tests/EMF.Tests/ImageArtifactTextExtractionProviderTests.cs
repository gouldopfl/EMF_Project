using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ImageArtifactTextExtractionProviderTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/tiff")]
    public void CanExtract_SupportsImageTypes(string contentType)
    {
        var provider =
            new ImageArtifactTextExtractionProvider(
                new StubContentStore([1, 2, 3]),
                new StubOcrService("recognized"));

        Assert.True(provider.CanExtract(contentType));
    }

    [Fact]
    public async Task ExtractTextAsync_DelegatesToOcrService()
    {
        var provider =
            new ImageArtifactTextExtractionProvider(
                new StubContentStore([1, 2, 3]),
                new StubOcrService("recognized text"));

        var result =
            await provider.ExtractTextAsync(
                new ArtifactId("image-001"));

        Assert.Equal("recognized text", result);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new ImageArtifactTextExtractionProvider(
                new StubContentStore(null),
                new StubOcrService("unused"));

        var result =
            await provider.ExtractTextAsync(
                new ArtifactId("image-missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new ImageArtifactTextExtractionProvider(
                new StubContentStore([1]),
                new StubOcrService("unused"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("image-cancelled"),
                cancellation.Token));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInput()
    {
        var provider =
            new ImageArtifactTextExtractionProvider(
                new StubContentStore(new byte[5]),
                new StubOcrService("unused"),
                maxInputBytes: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("image-too-large")));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedText()
    {
        var provider =
            new ImageArtifactTextExtractionProvider(
                new StubContentStore([1]),
                new StubOcrService("12345"),
                maxExtractedTextChars: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("image-text-too-large")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInputLimit(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImageArtifactTextExtractionProvider(
                new StubContentStore(null),
                new StubOcrService("unused"),
                maxInputBytes: value));
    }


    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveTextLimit(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImageArtifactTextExtractionProvider(
                new StubContentStore(null),
                new StubOcrService("unused"),
                maxExtractedTextChars: value));
    }


    private sealed class StubOcrService :
        IImageOcrService
    {
        private readonly string _text;

        public StubOcrService(string text)
        {
            _text = text;
        }

        public Task<string?> RecognizeTextAsync(
            OcrRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(_text);
        }
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
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_content);
        }

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
