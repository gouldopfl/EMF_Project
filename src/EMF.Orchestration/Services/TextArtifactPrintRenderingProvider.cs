using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class TextArtifactPrintRenderingProvider :
    IArtifactPrintRenderingProvider
{
    public const long DefaultMaxInputBytes =
        10L * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;

    public TextArtifactPrintRenderingProvider(
        IArtifactContentStore contentStore,
        long maxInputBytes = DefaultMaxInputBytes)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        if (maxInputBytes <= 0 ||
            maxInputBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputBytes));
        }

        _contentStore = contentStore;
        _maxInputBytes = maxInputBytes;
    }

    public bool CanRender(string contentType) =>
        string.Equals(
            contentType,
            "text/plain",
            StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        if (content.LongLength > _maxInputBytes)
        {
            throw new InvalidDataException(
                "Text print input exceeds the maximum allowed size.");
        }

        return
        [
            new PrintableArtifactPage
            {
                PageNumber = 1,
                ContentType = "text/plain",
                Content = content
            }
        ];
    }
}
