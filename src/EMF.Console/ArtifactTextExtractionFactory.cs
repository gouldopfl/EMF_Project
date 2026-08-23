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
                new PdfArtifactTextExtractionProvider(contentStore),
                new XlsxArtifactTextExtractionProvider(contentStore),
                new EmlArtifactTextExtractionProvider(contentStore),
                new MsgArtifactTextExtractionProvider(
                    contentStore,
                    new OutlookMessageDecoder()),
                new ImageArtifactTextExtractionProvider(
                    contentStore,
                    new PaddleImageOcrService())
            });
    }
}
