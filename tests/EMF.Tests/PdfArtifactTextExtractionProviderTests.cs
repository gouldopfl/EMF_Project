using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class PdfArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ExtractsTextFromPdf()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.pdf");

        var content =
            await File.ReadAllBytesAsync(path);

        var artifactId =
            new ArtifactId("pdf-001");

        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(artifactId);

        Assert.NotNull(text);
        Assert.Contains(
            "Veteran has chronic instability.",
            text);
        Assert.Contains(
            "MRI confirms lumbar degenerative changes.",
            text);
        Assert.Contains(
            "Second page contains additional evidence.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(null));

        var result =
            await provider.ExtractTextAsync(
                new ArtifactId("pdf-missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    Array.Empty<byte>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("pdf-cancelled"),
                cancellation.Token));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsMalformedPdf()
    {
        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    "not a PDF"u8.ToArray()));

        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.ExtractTextAsync(
                new ArtifactId("pdf-invalid")));
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
            return Task.FromResult<byte[]?>(_content);
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
