using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using SkiaSharp;
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


    [Fact]
    public async Task ExtractTextAsync_DoesNotOcrEmbeddedTextPage()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.pdf");

        var content =
            await File.ReadAllBytesAsync(path);

        var renderer = new StubPageRenderer();
        var ocr = new StubOcrService("should not appear");

        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(content),
                renderer,
                ocr);

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("pdf-text"));

        Assert.Contains(
            "Veteran has chronic instability.",
            text);

        Assert.Equal(0, renderer.CallCount);
        Assert.Equal(0, ocr.CallCount);
    }

    [Fact]
    public async Task ExtractTextAsync_OcrsTextlessPage()
    {
        var renderer = new StubPageRenderer();
        var ocr =
            new StubOcrService(
                "Scanned veteran evidence.");

        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateTextlessPdf()),
                renderer,
                ocr);

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("pdf-scan"));

        Assert.Equal(
            "Scanned veteran evidence.",
            text);

        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(1, ocr.CallCount);
    }

    [Fact]
    public void Constructor_RejectsIncompleteOcrFallback()
    {
        Assert.Throws<ArgumentException>(
            () => new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    Array.Empty<byte>()),
                new StubPageRenderer()));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyPages()
    {
        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateTextlessPdf(2)),
                maxPageCount: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("pdf-page-count")));

        Assert.Equal(
            "PDF exceeds the maximum allowed page count.",
            ex.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyOcrPages()
    {
        var renderer = new StubPageRenderer();
        var ocr = new StubOcrService("Scanned evidence.");

        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateTextlessPdf(2)),
                renderer,
                ocr,
                maxOcrPageCount: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("pdf-ocr-page-count")));

        Assert.Equal(
            "PDF exceeds the maximum allowed OCR page count.",
            ex.Message);

        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(1, ocr.CallCount);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedRenderedPage()
    {
        var renderer =
            new StubPageRenderer(
                new byte[] { 1, 2, 3, 4 });

        var provider =
            new PdfArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateTextlessPdf()),
                renderer,
                new StubOcrService("Scanned evidence."),
                maxRenderedPageBytes: 3);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("pdf-rendered-page-size")));

        Assert.Equal(
            "PDF rendered page exceeds the maximum allowed image size.",
            ex.Message);
    }

    [Theory]
    [InlineData(
        true,
        "PDF page text exceeds the maximum allowed size.")]
    [InlineData(
        false,
        "PDF extracted text exceeds the maximum allowed size.")]
    public async Task ExtractTextAsync_RejectsOversizedText(
        bool pageLimit,
        string expectedMessage)
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.pdf");

        var content =
            await File.ReadAllBytesAsync(path);

        var provider =
            pageLimit
                ? new PdfArtifactTextExtractionProvider(
                    new StubContentStore(content),
                    maxPageTextChars: 1)
                : new PdfArtifactTextExtractionProvider(
                    new StubContentStore(content),
                    maxExtractedTextChars: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("pdf-text-limit")));

        Assert.Equal(expectedMessage, ex.Message);
    }

    private static byte[] CreateTextlessPdf(
        int pageCount = 1)
    {
        using var output = new MemoryStream();

        using (var document =
            SKDocument.CreatePdf(output))
        {
            for (var index = 0;
                 index < pageCount;
                 index++)
            {
                document.BeginPage(612, 792);
                document.EndPage();
            }

            document.Close();
        }

        return output.ToArray();
    }

    private sealed class StubPageRenderer :
        IPdfPageImageRenderer
    {
        private readonly byte[] _image;

        public StubPageRenderer(byte[]? image = null)
        {
            _image =
                image ??
                new byte[] { 1, 2, 3 };
        }

        public int CallCount { get; private set; }

        public Task<byte[]> RenderPageAsync(
            ReadOnlyMemory<byte> pdf,
            int pageIndex,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return Task.FromResult(_image);
        }
    }

    private sealed class StubOcrService :
        IImageOcrService
    {
        private readonly string? _text;

        public StubOcrService(string? text)
        {
            _text = text;
        }

        public int CallCount { get; private set; }

        public Task<string?> RecognizeTextAsync(
            OcrRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return Task.FromResult(_text);
        }
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
