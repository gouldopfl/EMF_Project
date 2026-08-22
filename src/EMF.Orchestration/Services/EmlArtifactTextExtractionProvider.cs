using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class EmlArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private readonly IArtifactContentStore _contentStore;

    public EmlArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            "message/rfc822",
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

        var text =
            new UTF8Encoding(
                false,
                true)
            .GetString(content);

        var separator = text.IndexOf(
            "\r\n\r\n",
            StringComparison.Ordinal);

        var separatorLength = 4;

        if (separator < 0)
        {
            separator = text.IndexOf(
                "\n\n",
                StringComparison.Ordinal);

            separatorLength = 2;
        }

        if (separator < 0)
            throw new FormatException(
                "The EML message does not contain a header/body separator.");

        return text[(separator + separatorLength)..];
    }
}
