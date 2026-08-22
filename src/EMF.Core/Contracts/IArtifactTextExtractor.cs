using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IArtifactTextExtractor
{
    Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
