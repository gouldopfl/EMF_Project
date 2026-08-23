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
                new CsvArtifactTextExtractionProvider(contentStore),
                new JsonArtifactTextExtractionProvider(contentStore),
                new XmlArtifactTextExtractionProvider(contentStore),
                new DocxArtifactTextExtractionProvider(contentStore),
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
