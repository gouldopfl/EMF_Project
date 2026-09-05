using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class DocxArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsDocumentText()
    {
        var content = CreateDocument("Required evidence");
        var provider =
            new DocxArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("docx-1"));

        Assert.Contains("Required evidence", text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenMissing()
    {
        var provider =
            new DocxArtifactTextExtractionProvider(
                new StubContentStore(null));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("missing"));

        Assert.Null(text);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyPackageEntries()
    {
        var provider =
            new DocxArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateDocument("Evidence")),
                maxPackageEntryCount: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("docx-entry-count")));

        Assert.Equal(
            "DOCX package exceeds the maximum allowed entry count.",
            ex.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedPackageEntry()
    {
        var provider =
            new DocxArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateDocument("Evidence")),
                maxPackageEntryBytes: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("docx-entry-size")));

        Assert.Equal(
            "DOCX package entry exceeds the maximum allowed size.",
            ex.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedPackageTotal()
    {
        var provider =
            new DocxArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateDocument("Evidence")),
                maxPackageTotalBytes: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("docx-total-size")));

        Assert.Equal(
            "DOCX package exceeds the maximum allowed extracted size.",
            ex.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedExtractedText()
    {
        var provider =
            new DocxArtifactTextExtractionProvider(
                new StubContentStore(
                    CreateDocument("Evidence")),
                maxExtractedTextChars: 3);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("docx-text-size")));

        Assert.Equal(
            "DOCX extracted text exceeds the maximum allowed size.",
            ex.Message);
    }

    private static byte[] CreateDocument(string text)
    {
        using var stream = new MemoryStream();

        using (var document =
            WordprocessingDocument.Create(
                stream,
                WordprocessingDocumentType.Document))
        {
            var part = document.AddMainDocumentPart();

            part.Document =
                new Document(
                    new Body(
                        new Paragraph(
                            new Run(
                                new Text(text)))));
        }

        return stream.ToArray();
    }

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(byte[]? content) =>
            _content = content;

        public Task WriteAsync(
            ArtifactId id,
            ReadOnlyMemory<byte> content,
            CancellationToken c = default) =>
            Task.CompletedTask;

        public Task<byte[]?> ReadAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            Task.FromResult(_content);

        public Task DeleteAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            Task.CompletedTask;
    }
}
