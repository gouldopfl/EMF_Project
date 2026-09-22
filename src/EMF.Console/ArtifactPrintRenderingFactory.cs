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
        var pageRenderer =
            new PdfToImagePageRenderer(
                grayscale: false);

        var pdfProvider =
            new PdfArtifactPrintRenderingProvider(
                contentStore,
                pageRenderer);

        var docxProvider =
            new DocxArtifactPrintRenderingProvider(
                contentStore,
                new LibreOfficeVeteransReviewerPackageDocumentConverter(),
                pageRenderer);
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
            [pdfProvider, docxProvider, textProvider, imageProvider, htmlProvider]);
    }
}
