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
