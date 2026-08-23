using b2xtranslator.DocFileFormat;
using b2xtranslator.StructuredStorage.Reader;
using b2xtranslator.txt;
using b2xtranslator.txt.TextModel;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class DocArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/msword";

    private readonly IArtifactContentStore _contentStore;

    public DocArtifactTextExtractionProvider(
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

        cancellationToken.ThrowIfCancellationRequested();

        using var stream =
            new MemoryStream(content, writable: false);

        using var reader =
            new StructuredStorageReader(
                stream,
                logger: null);

        var document =
            new WordDocument(
                reader,
                fibFC: 0);

        var textDocument =
            TextDocument.Create(
                string.Empty,
                writer: null!,
                extractUrls: true);

        cancellationToken.ThrowIfCancellationRequested();

        return DocTextExtractor.ConvertToString(
            document,
            textDocument,
            extractUrls: true);
    }
}
