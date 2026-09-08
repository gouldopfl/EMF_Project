using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

public interface IArtifactPrintRenderingProvider
{
    bool CanRender(string contentType);

    Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
