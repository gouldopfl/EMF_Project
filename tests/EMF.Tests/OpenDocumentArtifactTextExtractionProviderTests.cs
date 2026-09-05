using System.IO.Compression;
using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class OpenDocumentArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReadsOdtText()
    {
        var content = CreateOdt();

        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(content),
                "application/vnd.oasis.opendocument.text",
                "evidence.odt");

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("odt-001"));

        Assert.Contains("EMF ODT Evidence", text);
        Assert.Contains("MRI confirms lumbar changes.", text);
    }

    [Fact]
    public async Task ExtractTextAsync_ReadsOdsText()
    {
        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(CreateOds()),
                "application/vnd.oasis.opendocument.spreadsheet",
                "evidence.ods");

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("ods-001"));

        Assert.Contains("EMF ODS Evidence", text);
        Assert.Contains("Lumbar MRI", text);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyPackageEntries()
    {
        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(CreateOdt()),
                "application/vnd.oasis.opendocument.text",
                "evidence.odt",
                maxPackageEntryCount: 2);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("odt-entry-count")));

        Assert.Equal(
            "OpenDocument package exceeds " +
            "the maximum allowed entry count.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedPackageEntry()
    {
        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(CreateOdt()),
                "application/vnd.oasis.opendocument.text",
                "evidence.odt",
                maxPackageEntryBytes: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("odt-entry-size")));

        Assert.Equal(
            "OpenDocument package entry exceeds " +
            "the maximum allowed size.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedPackageTotal()
    {
        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(CreateOdt()),
                "application/vnd.oasis.opendocument.text",
                "evidence.odt",
                maxPackageTotalBytes: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("odt-total-size")));

        Assert.Equal(
            "OpenDocument package exceeds " +
            "the maximum allowed extracted size.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedExtractedText()
    {
        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(CreateOdt()),
                "application/vnd.oasis.opendocument.text",
                "evidence.odt",
                maxExtractedTextChars: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("odt-text-size")));

        Assert.Equal(
            "OpenDocument extracted text exceeds " +
            "the maximum allowed size.",
            exception.Message);
    }

    private static byte[] CreateOds()
    {
        using var stream = new MemoryStream();

        using (var zip = new ZipArchive(
            stream, ZipArchiveMode.Create, true))
        {
            var mime = zip.CreateEntry(
                "mimetype",
                CompressionLevel.NoCompression);

            using (var writer = new StreamWriter(
                mime.Open(),
                new UTF8Encoding(false)))
            {
                writer.Write(
                    "application/vnd.oasis.opendocument.spreadsheet");
            }

            Add(zip, "META-INF/manifest.xml",
                """
                <manifest:manifest
                  xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0">
                  <manifest:file-entry manifest:full-path="/"
                    manifest:media-type="application/vnd.oasis.opendocument.spreadsheet"/>
                  <manifest:file-entry manifest:full-path="content.xml"
                    manifest:media-type="text/xml"/>
                </manifest:manifest>
                """);

            Add(zip, "content.xml",
                """
                <office:document-content
                  xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                  xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                  xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
                  <office:body><office:spreadsheet>
                    <table:table table:name="Evidence">
                      <table:table-row>
                        <table:table-cell><text:p>EMF ODS Evidence</text:p></table:table-cell>
                        <table:table-cell><text:p>Lumbar MRI</text:p></table:table-cell>
                      </table:table-row>
                    </table:table>
                  </office:spreadsheet></office:body>
                </office:document-content>
                """);
        }

        return stream.ToArray();
    }

    [Fact]
    public async Task ExtractTextAsync_ReadsOdpText()
    {
        var provider =
            new OpenDocumentArtifactTextExtractionProvider(
                new StubContentStore(CreateOdp()),
                "application/vnd.oasis.opendocument.presentation",
                "evidence.odp");

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("odp-001"));

        Assert.Contains("EMF ODP Evidence", text);
        Assert.Contains("Slide evidence text", text);
    }

    private static byte[] CreateOdp()
    {
        using var stream = new MemoryStream();

        using (var zip = new ZipArchive(
            stream, ZipArchiveMode.Create, true))
        {
            var mime = zip.CreateEntry(
                "mimetype",
                CompressionLevel.NoCompression);

            using (var writer = new StreamWriter(
                mime.Open(),
                new UTF8Encoding(false)))
            {
                writer.Write(
                    "application/vnd.oasis.opendocument.presentation");
            }

            Add(zip, "META-INF/manifest.xml",
                """
                <manifest:manifest
                  xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0">
                  <manifest:file-entry manifest:full-path="/"
                    manifest:media-type="application/vnd.oasis.opendocument.presentation"/>
                  <manifest:file-entry manifest:full-path="content.xml"
                    manifest:media-type="text/xml"/>
                </manifest:manifest>
                """);

            Add(zip, "content.xml",
                """
                <office:document-content
                  xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                  xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
                  xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                  xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0">
                  <office:body>
                    <office:presentation>
                      <draw:page draw:name="page1">
                        <draw:frame>
                          <draw:text-box>
                            <text:p>EMF ODP Evidence</text:p>
                            <text:p>Slide evidence text</text:p>
                          </draw:text-box>
                        </draw:frame>
                      </draw:page>
                    </office:presentation>
                  </office:body>
                </office:document-content>
                """);
        }

        return stream.ToArray();
    }

    private static byte[] CreateOdt()
    {
        using var stream = new MemoryStream();

        using (var zip =
            new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            var mimetype =
                zip.CreateEntry(
                    "mimetype",
                    CompressionLevel.NoCompression);

            using (var writer =
                new StreamWriter(
                    mimetype.Open(),
                    new UTF8Encoding(false),
                    leaveOpen: false))
            {
                writer.Write(
                    "application/vnd.oasis.opendocument.text");
            }

            Add(
                zip,
                "META-INF/manifest.xml",
                """
                <manifest:manifest
                  xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"
                  manifest:version="1.2">
                  <manifest:file-entry
                    manifest:full-path="/"
                    manifest:media-type="application/vnd.oasis.opendocument.text"/>
                  <manifest:file-entry
                    manifest:full-path="content.xml"
                    manifest:media-type="text/xml"/>
                </manifest:manifest>
                """);

            Add(
                zip,
                "content.xml",
                """
                <office:document-content
                  xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                  xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
                  <office:body>
                    <office:text>
                      <text:p>EMF ODT Evidence</text:p>
                      <text:p>MRI confirms lumbar changes.</text:p>
                    </office:text>
                  </office:body>
                </office:document-content>
                """);
        }

        return stream.ToArray();
    }

    private static void Add(
        ZipArchive zip,
        string name,
        string value)
    {
        var entry = zip.CreateEntry(name);

        using var writer =
            new StreamWriter(
                entry.Open(),
                Encoding.UTF8);

        writer.Write(value);
    }

    private sealed class StubContentStore(byte[] content) :
        IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(content);

        public Task WriteAsync(
            ArtifactId id,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
