using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using OfficeIMO.Reader;
using OfficeIMO.Reader.OpenDocument;

namespace EMF.Orchestration.Services;

public sealed class OpenDocumentArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private readonly IArtifactContentStore _contentStore;
    private readonly OfficeDocumentReader _reader;
    private readonly string _contentType;
    private readonly string _fileName;

    public OpenDocumentArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        string contentType,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        _contentStore = contentStore;
        _contentType = contentType;
        _fileName = fileName;

        _reader =
            new OfficeDocumentReaderBuilder()
                .AddOpenDocumentHandler()
                .Build();
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            _contentType,
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

        var result =
            _reader.ReadDocument(
                content,
                _fileName,
                options: null,
                cancellationToken);

        return result.Markdown;
    }
}
