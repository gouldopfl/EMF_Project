using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class MsgArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsMessageBody()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "TxtSampleEmail.msg");

        var content =
            await File.ReadAllBytesAsync(path);

        var provider =
            new MsgArtifactTextExtractionProvider(
                new StubContentStore(content),
                new OutlookMessageDecoder());

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("msg-1"));

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenMissing()
    {
        var provider =
            new MsgArtifactTextExtractionProvider(
                new StubContentStore(null),
                new OutlookMessageDecoder());

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("missing"));

        Assert.Null(text);
    }

    [Fact]
    public void CanExtract_RecognizesOutlookMessage()
    {
        var provider =
            new MsgArtifactTextExtractionProvider(
                new StubContentStore(null),
                new OutlookMessageDecoder());

        Assert.True(
            provider.CanExtract("application/vnd.ms-outlook"));

        Assert.False(provider.CanExtract("message/rfc822"));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInputBeforeDecode()
    {
        var decoder =
            new StubOutlookMessageDecoder(
                new DecodedOutlookMessage
                {
                    BodyText = "unused"
                });

        var provider =
            new MsgArtifactTextExtractionProvider(
                new StubContentStore([1, 2, 3, 4, 5]),
                decoder,
                maxInputBytes: 4,
                maxExtractedTextChars: 100);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("msg-oversized-input")));

        Assert.False(decoder.WasCalled);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedBodyText()
    {
        var decoder =
            new StubOutlookMessageDecoder(
                new DecodedOutlookMessage
                {
                    BodyText = "12345"
                });

        var provider =
            new MsgArtifactTextExtractionProvider(
                new StubContentStore([1]),
                decoder,
                maxInputBytes: 100,
                maxExtractedTextChars: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("msg-oversized-text")));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedBodyHtml()
    {
        var decoder =
            new StubOutlookMessageDecoder(
                new DecodedOutlookMessage
                {
                    BodyText = null,
                    BodyHtml = "12345"
                });

        var provider =
            new MsgArtifactTextExtractionProvider(
                new StubContentStore([1]),
                decoder,
                maxInputBytes: 100,
                maxExtractedTextChars: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("msg-oversized-html")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInputLimit(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MsgArtifactTextExtractionProvider(
                new StubContentStore(null),
                new StubOutlookMessageDecoder(
                    new DecodedOutlookMessage()),
                maxInputBytes: value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveTextLimit(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MsgArtifactTextExtractionProvider(
                new StubContentStore(null),
                new StubOutlookMessageDecoder(
                    new DecodedOutlookMessage()),
                maxExtractedTextChars: value));
    }

    private sealed class StubOutlookMessageDecoder :
        IOutlookMessageDecoder
    {
        private readonly DecodedOutlookMessage _message;

        public StubOutlookMessageDecoder(
            DecodedOutlookMessage message)
        {
            _message = message;
        }

        public bool WasCalled { get; private set; }

        public Task<DecodedOutlookMessage> DecodeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasCalled = true;
            return Task.FromResult(_message);
        }
    }


    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(byte[]? content) =>
            _content = content;

        public Task<byte[]?> ReadAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            Task.FromResult(_content);

        public Task WriteAsync(
            ArtifactId id,
            ReadOnlyMemory<byte> content,
            CancellationToken c = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            Task.CompletedTask;
    }
}
