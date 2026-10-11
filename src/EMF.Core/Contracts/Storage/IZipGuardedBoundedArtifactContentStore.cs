using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Storage;

/// <summary>Optional ZIP capability accepting current authority at physical read boundaries.</summary>
public interface IZipGuardedBoundedArtifactContentStore
{
    Task<IArtifactContentReadLease?> ReadBoundedVersionedGuardedAsync(ArtifactId artifactId,
        BoundedArtifactContentReadRequest request, IZipEffectBoundaryGuard guard,
        CancellationToken cancellationToken = default);
}
