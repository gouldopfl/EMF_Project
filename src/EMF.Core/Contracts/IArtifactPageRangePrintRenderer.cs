using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts;

/// <summary>Renders a bounded range using the original source page numbers.</summary>
public interface IArtifactPageRangePrintRenderer
{
    Task<IReadOnlyList<PrintableArtifactPage>> RenderRangeAsync(
        ArtifactId artifactId, int startPage, int endPage,
        CancellationToken cancellationToken = default);
}
