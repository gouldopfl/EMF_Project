using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Services;

namespace EMF.ConsoleApplication;

internal static class ArtifactPrintRenderingFactory
{
    public static IArtifactPrintRenderer Create(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);

#pragma warning disable CA1416
        var pdfProvider =
            new PdfArtifactPrintRenderingProvider(
                contentStore,
                new PdfToImagePageRenderer(
                    grayscale: false));
#pragma warning restore CA1416

        var textProvider =
            new TextArtifactPrintRenderingProvider(
                contentStore);

        var imageProvider =
            new ImageArtifactPrintRenderingProvider(
                contentStore);

        var htmlProvider =
            new HtmlArtifactPrintRenderingProvider(
                new HtmlArtifactTextExtractionProvider(
                    contentStore));

        return new ArtifactPrintRendererRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [pdfProvider, textProvider, imageProvider, htmlProvider]);
    }
}
