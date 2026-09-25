using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ArtifactPrintRendererRouter :
    IArtifactPrintRenderer, IArtifactPageRangePrintRenderer
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
        var provider = await ResolveProviderAsync(artifactId, cancellationToken);
        return provider is null ? [] : await provider.RenderAsync(artifactId, cancellationToken);
    }

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderRangeAsync(
        ArtifactId artifactId, int startPage, int endPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPage, startPage);
        var provider = await ResolveProviderAsync(artifactId, cancellationToken);
        if (provider is null)
            return [];
        if (provider is IArtifactPageRangePrintRenderer ranged)
            return await ranged.RenderRangeAsync(artifactId, startPage, endPage, cancellationToken);
        var pages = await provider.RenderAsync(artifactId, cancellationToken);
        return pages.Where(page => page.PageNumber >= startPage && page.PageNumber <= endPage).ToArray();
    }

    private async Task<IArtifactPrintRenderingProvider?> ResolveProviderAsync(
        ArtifactId artifactId, CancellationToken cancellationToken)
    {
        var artifact =
            await _repository.GetArtifactAsync(
                artifactId,
                cancellationToken);

        if (artifact is null)
            return null;

        if (artifact.Id != artifactId)
            throw new InvalidOperationException(
                "Artifact identity mismatch.");

        var contentType =
            _resolver.ResolveContentType(artifact);

        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var provider =
            _providers.FirstOrDefault(
                candidate => candidate.CanRender(contentType));

        if (provider is null)
        {
            throw new NotSupportedException(
                $"No print rendering provider supports '{contentType}'.");
        }

        return provider;
    }
}
