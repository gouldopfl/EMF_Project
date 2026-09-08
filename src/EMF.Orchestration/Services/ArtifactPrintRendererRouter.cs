using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ArtifactPrintRendererRouter :
    IArtifactPrintRenderer
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentTypeResolver _resolver;
    private readonly IReadOnlyList<IArtifactPrintRenderingProvider> _providers;

    public ArtifactPrintRendererRouter(
        IEvidenceRepository repository,
        IArtifactContentTypeResolver resolver,
        IEnumerable<IArtifactPrintRenderingProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(providers);

        _repository = repository;
        _resolver = resolver;
        _providers = providers.ToArray();
    }

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var artifact =
            await _repository.GetArtifactAsync(
                artifactId,
                cancellationToken);

        if (artifact is null)
            return [];

        if (artifact.Id != artifactId)
            throw new InvalidOperationException(
                "Artifact identity mismatch.");

        var contentType =
            _resolver.ResolveContentType(artifact);

        if (string.IsNullOrWhiteSpace(contentType))
            return [];

        var provider =
            _providers.FirstOrDefault(
                candidate => candidate.CanRender(contentType));

        if (provider is null)
        {
            throw new NotSupportedException(
                $"No print rendering provider supports '{contentType}'.");
        }

        return await provider.RenderAsync(
            artifactId,
            cancellationToken);
    }
}
