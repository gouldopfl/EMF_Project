using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class RtfArtifactTextExtractionProviderTests
{
    [Fact]
    public void CanExtract_RecognizesRtf()
    {
        var provider =
            new RtfArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(
            provider.CanExtract("application/rtf"));
    }

    [Fact]
    public async Task ExtractTextAsync_ReadsRtfDocument()
    {
        const string rtf =
            @"{\rtf1\ansi " +
            @"EMF RTF Evidence Test\par " +
            @"MRI confirms lumbar changes.\par " +
            @"Pantoprazole medication history.}";

        var provider =
            new RtfArtifactTextExtractionProvider(
                new StubContentStore(
                    Encoding.ASCII.GetBytes(rtf)));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("artifact-rtf-001"));

        Assert.NotNull(text);
        Assert.Contains(
            "EMF RTF Evidence Test",
            text);
        Assert.Contains(
            "MRI confirms lumbar changes.",
            text);
        Assert.Contains(
            "Pantoprazole medication history.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new RtfArtifactTextExtractionProvider(
                new StubContentStore(null));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("artifact-rtf-missing"));

        Assert.Null(text);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        var provider =
            new RtfArtifactTextExtractionProvider(
                new StubContentStore([1, 2, 3]));

        using var source =
            new CancellationTokenSource();

        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("artifact-rtf-cancel"),
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
