using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class MsgArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.ms-outlook";

    private readonly IArtifactContentStore _contentStore;
    private readonly IOutlookMessageDecoder _decoder;

    public MsgArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IOutlookMessageDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(decoder);

        _contentStore = contentStore;
        _decoder = decoder;
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

        var message =
            await _decoder.DecodeAsync(
                content,
                cancellationToken);

        return !string.IsNullOrWhiteSpace(message.BodyText)
            ? message.BodyText
            : message.BodyHtml;
    }
}
