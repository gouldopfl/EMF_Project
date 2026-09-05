using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class XlsArtifactTextExtractionProviderTests
{
    [Fact]
    public void CanExtract_RecognizesLegacyExcelContentType()
    {
        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(
            provider.CanExtract(
                "application/vnd.ms-excel"));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyRows()
    {
        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(ReadSample()),
                maxRowCount: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("xls-row-limit")));

        Assert.Equal(
            "XLS workbook exceeds the maximum " +
            "allowed row count.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyCells()
    {
        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(ReadSample()),
                maxCellCount: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("xls-cell-limit")));

        Assert.Equal(
            "XLS workbook exceeds the maximum " +
            "allowed cell count.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_AllowsWorksheetLimitBoundary()
    {
        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(ReadSample()),
                maxWorksheetCount: 1);

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("xls-worksheet-boundary"));

        Assert.NotNull(text);
        Assert.NotEmpty(text);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedExtractedText()
    {
        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(ReadSample()),
                maxExtractedTextChars: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("xls-text-limit")));

        Assert.Equal(
            "XLS extracted text exceeds " +
            "the maximum allowed size.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_ExtractsTextFromMicrosoftXls()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xls");

        var content =
            await File.ReadAllBytesAsync(path);

        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("xls-001"));

        Assert.NotNull(text);
        Assert.NotEmpty(text);

        Assert.Contains(
            "ATTENDANCE (Pg 5)",
            text);

        Assert.Contains(
            "125 Strange, Bill",
            text);

        Assert.Contains(
            "148 Wright, T.J.",
            text);

        Assert.Contains(
            "Total in Attendance",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(null));

        var result =
            await provider.ExtractTextAsync(
                new ArtifactId("xls-missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new XlsArtifactTextExtractionProvider(
                new StubContentStore(
                    Array.Empty<byte>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("xls-cancelled"),
                cancellation.Token));
    }

    private static byte[] ReadSample() =>
        File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xls"));

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
