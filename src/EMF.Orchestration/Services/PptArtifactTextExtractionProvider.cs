using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using OfficeIMO.Reader;
using OfficeIMO.Reader.PowerPoint;

namespace EMF.Orchestration.Services;

public sealed class PptArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.ms-powerpoint";

    private readonly IArtifactContentStore _contentStore;
    private readonly OfficeDocumentReader _reader;

    public PptArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        _contentStore = contentStore;
        _reader =
            new OfficeDocumentReaderBuilder()
                .AddPowerPointHandler()
                .Build();
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

        var result =
            await _reader.ReadDocumentAsync(
                content,
                "evidence.ppt",
                options: null,
                cancellationToken);

        return result.Markdown;
    }
}
