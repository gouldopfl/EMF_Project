using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IArtifactDiscoveryProvider
{
    bool CanDiscover(string contentType);

    Task<ArtifactDiscoveryResult?> DiscoverAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
