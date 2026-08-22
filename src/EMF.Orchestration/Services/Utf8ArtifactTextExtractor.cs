using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class Utf8ArtifactTextExtractor :
    IArtifactTextExtractor
{
    private readonly IArtifactContentStore _contentStore;

    public Utf8ArtifactTextExtractor(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

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

        return new UTF8Encoding(
            false,
            true)
            .GetString(content);
    }
}
