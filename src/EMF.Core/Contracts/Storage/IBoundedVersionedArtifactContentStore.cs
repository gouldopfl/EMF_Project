using EMF.Core.Models.Identities;
namespace EMF.Core.Contracts.Storage;

/// <summary>Optional capability. Implementations must enforce bounds before payload allocation.</summary>
public interface IBoundedVersionedArtifactContentStore
{
    long MaximumStoredRepresentationBytes { get; }
    Task<IArtifactContentReadLease?> ReadBoundedVersionedAsync(ArtifactId artifactId,
        BoundedArtifactContentReadRequest request, CancellationToken cancellationToken = default);
}
