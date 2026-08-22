using EMF.Core.Contracts;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ArtifactTextExtractorRouter :
    IArtifactTextExtractor
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentTypeResolver _resolver;
    private readonly IReadOnlyList<IArtifactTextExtractionProvider> _providers;

    public ArtifactTextExtractorRouter(
        IEvidenceRepository repository,
        IArtifactContentTypeResolver resolver,
        IEnumerable<IArtifactTextExtractionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(providers);

        _repository = repository;
        _resolver = resolver;
        _providers = providers.ToArray();
    }

    public async Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var artifact =
            await _repository.GetArtifactAsync(
                artifactId,
                cancellationToken);

        if (artifact is null)
            return null;

        var contentType =
            _resolver.ResolveContentType(artifact);

        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var provider =
            _providers.FirstOrDefault(
                candidate =>
                    candidate.CanExtract(contentType));

        if (provider is null)
        {
            throw new NotSupportedException(
                $"No text extraction provider supports '{contentType}'.");
        }

        return await provider.ExtractTextAsync(
            artifactId,
            cancellationToken);
    }
}
