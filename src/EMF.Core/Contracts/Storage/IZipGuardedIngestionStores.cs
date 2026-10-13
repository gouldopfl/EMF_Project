using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Storage;

// Optional provider capabilities required by ZIP child ingestion. Implementations
// must check the guard at their actual I/O and commit boundaries.
public interface IZipGuardedArtifactContentStagingStore
{
    Task StageGuardedAsync(ArtifactContentOperationId operation, ReadOnlyMemory<byte> candidate,
        IZipEffectBoundaryGuard guard, CancellationToken ct = default);
    Task<byte[]?> ReadGuardedAsync(ArtifactContentOperationId operation,
        IZipEffectBoundaryGuard guard, CancellationToken ct = default);
}

public interface IZipGuardedPreparedArtifactContentStore : IZipGuardedBoundedArtifactContentStore
{
    Task<IPreparedArtifactContentMutation> PreparePhysicalCreateGuardedAsync(ArtifactId id,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context,
        IZipEffectBoundaryGuard guard, CancellationToken ct = default);
}
