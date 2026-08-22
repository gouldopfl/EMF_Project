using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ArtifactDiscoveryRouter
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentTypeResolver _resolver;
    private readonly IReadOnlyList<IArtifactDiscoveryProvider> _providers;

    public ArtifactDiscoveryRouter(
        IEvidenceRepository repository,
        IArtifactContentTypeResolver resolver,
        IEnumerable<IArtifactDiscoveryProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(providers);
        _repository = repository;
        _resolver = resolver;
        _providers = providers.ToArray();
    }

    public async Task<ArtifactDiscoveryResult?> DiscoverAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _repository.GetArtifactAsync(
            artifactId, cancellationToken);

        if (artifact is null)
            return null;

        var contentType = _resolver.ResolveContentType(artifact);

        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var provider = _providers.FirstOrDefault(
            candidate => candidate.CanDiscover(contentType));

        if (provider is null)
            throw new NotSupportedException(
                $"No discovery provider supports '{contentType}'.");

        return await provider.DiscoverAsync(
            artifactId, cancellationToken);
    }
}
