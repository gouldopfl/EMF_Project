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

    [Theory]
    [InlineData(
        "package-entries",
        "XLSX package exceeds the maximum allowed entry count.")]
    [InlineData(
        "package-entry-size",
        "XLSX package entry exceeds the maximum allowed size.")]
    [InlineData(
        "package-total-size",
        "XLSX package exceeds the maximum allowed extracted size.")]
    [InlineData(
        "worksheets",
        "XLSX exceeds the maximum allowed worksheet count.")]
    [InlineData(
        "rows",
        "XLSX exceeds the maximum allowed row count.")]
    [InlineData(
        "cells",
        "XLSX exceeds the maximum allowed cell count.")]
    [InlineData(
        "text",
        "XLSX extracted text exceeds the maximum allowed size.")]
    public async Task ExtractTextAsync_EnforcesResourceLimit(
        string limit,
        string expectedMessage)
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xlsx");

        var content =
            await File.ReadAllBytesAsync(path);

        var maxPackageEntryCount =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxPackageEntryCount;
        var maxPackageEntryBytes =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxPackageEntryBytes;
        var maxPackageTotalBytes =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxPackageTotalBytes;
        var maxWorksheetCount =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxWorksheetCount;
        var maxRowCount =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxRowCount;
        var maxCellCount =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxCellCount;
        var maxExtractedTextChars =
            XlsxArtifactTextExtractionProvider
                .DefaultMaxExtractedTextChars;

        switch (limit)
        {
            case "package-entries":
                maxPackageEntryCount = 1;
                break;
            case "package-entry-size":
                maxPackageEntryBytes = 1;
                break;
            case "package-total-size":
                maxPackageTotalBytes = 1;
                break;
            case "worksheets":
                maxWorksheetCount = 1;
                break;
            case "rows":
                maxRowCount = 1;
                break;
            case "cells":
                maxCellCount = 1;
                break;
            case "text":
                maxExtractedTextChars = 1;
                break;
        }

        var provider =
            new XlsxArtifactTextExtractionProvider(
                new StubContentStore(content),
                maxPackageEntryCount: maxPackageEntryCount,
                maxPackageEntryBytes: maxPackageEntryBytes,
                maxPackageTotalBytes: maxPackageTotalBytes,
                maxWorksheetCount: maxWorksheetCount,
                maxRowCount: maxRowCount,
                maxCellCount: maxCellCount,
                maxExtractedTextChars: maxExtractedTextChars);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId($"xlsx-{limit}")));

        Assert.Equal(expectedMessage, ex.Message);
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
