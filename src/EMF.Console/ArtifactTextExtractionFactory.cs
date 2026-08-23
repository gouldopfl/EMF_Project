using System.Runtime.Versioning;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Services;

namespace EMF.ConsoleApplication;

internal static class ArtifactTextExtractionFactory
{
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static IArtifactTextExtractor Create(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);

        var ocrService =
            new PaddleImageOcrService();

        return new ArtifactTextExtractorRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            new IArtifactTextExtractionProvider[]
            {
                new Utf8ArtifactTextExtractor(contentStore),
                new RtfArtifactTextExtractionProvider(contentStore),
                new OpenDocumentArtifactTextExtractionProvider(
                    contentStore,
                    "application/vnd.oasis.opendocument.text",
                    "evidence.odt"),
                new OpenDocumentArtifactTextExtractionProvider(
                    contentStore,
                    "application/vnd.oasis.opendocument.spreadsheet",
                    "evidence.ods"),
                new OpenDocumentArtifactTextExtractionProvider(
                    contentStore,
                    "application/vnd.oasis.opendocument.presentation",
                    "evidence.odp"),
                new CsvArtifactTextExtractionProvider(contentStore),
                new JsonArtifactTextExtractionProvider(contentStore),
                new XmlArtifactTextExtractionProvider(contentStore),
                new HtmlArtifactTextExtractionProvider(contentStore),
                new DocxArtifactTextExtractionProvider(contentStore),
                new DocArtifactTextExtractionProvider(contentStore),
                new PptxArtifactTextExtractionProvider(contentStore),
                new PptArtifactTextExtractionProvider(contentStore),
                new PdfArtifactTextExtractionProvider(
                    contentStore,
                    new PdfToImagePageRenderer(),
                    ocrService),
                new XlsxArtifactTextExtractionProvider(contentStore),
                new XlsArtifactTextExtractionProvider(contentStore),
                new EmlArtifactTextExtractionProvider(contentStore),
                new MsgArtifactTextExtractionProvider(
                    contentStore,
                    new OutlookMessageDecoder()),
                new ImageArtifactTextExtractionProvider(
                    contentStore,
                    ocrService)
            });
    }
}
