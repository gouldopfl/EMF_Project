using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class PptArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReadsLegacyPowerPointText()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.ppt");

        var content =
            await File.ReadAllBytesAsync(path);

        var provider =
            new PptArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("ppt-001"));

        Assert.NotNull(text);
        Assert.Contains(
            "EMF Legacy PowerPoint Evidence Test",
            text);
        Assert.Contains(
            "MRI confirms lumbar degenerative changes.",
            text);
    }

    [Fact]
    public void CanExtract_AcceptsLegacyPowerPointContentType()
    {
        var provider =
            new PptArtifactTextExtractionProvider(
                new StubContentStore([]));

        Assert.True(
            provider.CanExtract(
                "application/vnd.ms-powerpoint"));
    }

    private sealed class StubContentStore(byte[] content) :
        IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(content);

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
