using System.Text;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class DocxArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly IArtifactContentStore _contentStore;

    public DocxArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            ContentType,
            StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return null;

        using var stream =
            new MemoryStream(content, writable: false);

        using var document =
            WordprocessingDocument.Open(stream, false);

        var body =
            document.MainDocumentPart?
                .Document?
                .Body;

        if (body is null)
            return string.Empty;

        return body.InnerText;
    }
}
