using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Services;

namespace EMF.ConsoleApplication;

internal sealed class DocxArtifactPrintRenderingProvider :
    IArtifactPrintRenderingProvider
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly IArtifactContentStore _contentStore;
    private readonly IVeteransReviewerPackageDocumentConverter _converter;
    private readonly IPdfPageImageRenderer _pageRenderer;

    public DocxArtifactPrintRenderingProvider(
        IArtifactContentStore contentStore,
        IVeteransReviewerPackageDocumentConverter converter,
        IPdfPageImageRenderer pageRenderer)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(pageRenderer);

        _contentStore = contentStore;
        _converter = converter;
        _pageRenderer = pageRenderer;
    }

    public bool CanRender(string contentType) =>
        string.Equals(
            contentType,
            DocxContentType,
            StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var docx =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (docx is null)
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        var pdf =
            await _converter.ConvertDocxToPdfAsync(
                docx,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var convertedStore =
            new ConvertedPdfContentStore(
                artifactId,
                pdf);

        return await new PdfArtifactPrintRenderingProvider(
                convertedStore,
                _pageRenderer)
            .RenderAsync(
                artifactId,
                cancellationToken);
    }

    private sealed class ConvertedPdfContentStore :
        IArtifactContentStore
    {
        private readonly ArtifactId _artifactId;
        private readonly byte[] _content;

        public ConvertedPdfContentStore(
            ArtifactId artifactId,
            byte[] content)
        {
            ArgumentNullException.ThrowIfNull(content);
            _artifactId = artifactId;
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<byte[]?>(
                artifactId == _artifactId
                    ? _content
                    : null);
        }

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Converted PDF content store is read-only.");

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Converted PDF content store is read-only.");
    }
}
