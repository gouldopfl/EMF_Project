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
