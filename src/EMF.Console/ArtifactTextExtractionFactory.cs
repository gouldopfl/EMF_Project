using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Services;

namespace EMF.ConsoleApplication;

internal static class ArtifactTextExtractionFactory
{
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
#pragma warning disable CA1416
                new PdfArtifactTextExtractionProvider(
                    contentStore,
                    new PdfToImagePageRenderer(),
                    ocrService),
#pragma warning restore CA1416
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
