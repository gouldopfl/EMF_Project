using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IArtifactTextExtractionProvider
{
    bool CanExtract(string contentType);

    Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
