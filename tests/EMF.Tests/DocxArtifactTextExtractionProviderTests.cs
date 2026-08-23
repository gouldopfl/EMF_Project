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
