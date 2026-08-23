using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class DocArtifactTextExtractionProviderTests
{
    [Fact]
    public void CanExtract_RecognizesLegacyWord()
    {
        var provider =
            new DocArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(
            provider.CanExtract("application/msword"));
    }

    [Fact]
    public async Task ExtractTextAsync_ReadsLegacyWordDocument()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.doc");

        var content =
            await File.ReadAllBytesAsync(path);

        var provider =
            new DocArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("artifact-doc-001"));

        Assert.NotNull(text);
        Assert.NotEmpty(text);
        Assert.Contains(
            "VA-Blue-Button-report-Michael-Gould-7-30-2026_1225pm(5).pdf",
            text);
        Assert.Contains(
            "Excellent. We now have the two cornerstone documents",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new DocArtifactTextExtractionProvider(
                new StubContentStore(null));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("artifact-doc-missing"));

        Assert.Null(text);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        var provider =
            new DocArtifactTextExtractionProvider(
                new StubContentStore([1, 2, 3]));

        using var source =
            new CancellationTokenSource();

        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("artifact-doc-cancel"),
                source.Token));
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
