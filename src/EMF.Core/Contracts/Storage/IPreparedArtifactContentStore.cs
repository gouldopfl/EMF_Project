using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Storage;

// Optional admitted capability: expensive generation preparation precedes the security fence.
// Providers enforce an admitted inspection work/size/deadline profile across
// admission and all detached checks. Cancellation or exhaustion must release
// owned resources and cannot return a partially validated promotion handle.
// The handle owns existing provider generation/GC coordination, never authorization.
public interface IPreparedArtifactContentStore : IVersionedArtifactContentStore
{
    Task<IPreparedArtifactContentMutation> PreparePhysicalCreateAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default);
    Task<IPreparedArtifactContentMutation> PreparePhysicalDeleteAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default);
    // Validated current generation identity without copying payload. Missing/tombstone returns null.
    Task<IArtifactContentRevisionProbe> PrepareRevisionValidationAsync(ArtifactId id, CancellationToken cancellationToken = default);
    Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(ArtifactId id, CancellationToken cancellationToken = default);
}
public interface IPreparedArtifactContentMutation : IAsyncDisposable
{
    Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken cancellationToken = default);
}

// Full provider admission/integrity occurs before acquiring metadata coordination.
// This operation-owned handle retains no read/write transaction; checking copies no payload.
public interface IArtifactContentRevisionProbe : IAsyncDisposable
{
    Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(CancellationToken cancellationToken = default);
}
