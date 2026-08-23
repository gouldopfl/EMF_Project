using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
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
