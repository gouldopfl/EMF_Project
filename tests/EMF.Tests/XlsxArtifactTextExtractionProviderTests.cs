using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class XlsxArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ExtractsMultipleWorksheets()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xlsx");

        var content =
            await File.ReadAllBytesAsync(path);

        var provider =
            new XlsxArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("xlsx-001"));

        Assert.NotNull(text);

        Assert.Contains(
            "[Worksheet: Evidence]",
            text);

        Assert.Contains(
            "A1: Evidence Summary",
            text);

        Assert.Contains(
            "B1: MRI confirms lumbar changes.",
            text);

        Assert.Contains(
            "C1: Shared string evidence value",
            text);

        Assert.Contains(
            "[Worksheet: Medications]",
            text);

        Assert.Contains(
            "A1: Medication",
            text);

        Assert.Contains(
            "B1: Pantoprazole",
            text);

        Assert.Contains(
            "A2: Dose",
            text);

        Assert.Contains(
            "B2: 80 mg daily",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new XlsxArtifactTextExtractionProvider(
                new StubContentStore(null));

        var result =
            await provider.ExtractTextAsync(
                new ArtifactId("xlsx-missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new XlsxArtifactTextExtractionProvider(
                new StubContentStore(
                    Array.Empty<byte>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("xlsx-cancelled"),
                cancellation.Token));
    }

    [Fact]
    public async Task CanExtract_RecognizesXlsxContentType()
    {
        var provider =
            new XlsxArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(
            provider.CanExtract(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        Assert.False(
            provider.CanExtract("application/pdf"));
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
