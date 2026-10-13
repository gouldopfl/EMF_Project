using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Zip;

public enum ZipRetentionState { Reserved, Preparing, CandidateBound, Created, ReleasePending, Released, RequiresReview }
public sealed record ZipRetentionIdentity(string ParentOperationId,int FileOrdinal,string ObjectId,
    string CreateOperationId,string ReleaseOperationId,string OwnerToken,string NamespaceId,string PlaintextSha256,long Length);
public sealed record ZipCandidateCleanupReceipt(string CreateOperationId,string CandidateHash,bool AlreadyAbsent);
public sealed record ZipRetentionRecord(ZipRetentionIdentity Identity,ZipRetentionState State=ZipRetentionState.Reserved,
    long Revision=1,string? CandidateHash=null,ArtifactContentMutationReceipt? CreateReceipt=null,
    ArtifactContentMutationReceipt? ReleaseReceipt=null,ZipCandidateCleanupReceipt? CandidateCleanup=null,string? SafeReason=null);
public sealed record ZipRetentionUpdate(ZipParentSnapshot Parent,ZipRetentionRecord Retention);
// Release consumes this immutable occurrence proof; insertion belongs to the fenced ACK stage.
public sealed record ZipAcknowledgementBinding(string ParentOperationId,int FileOrdinal,string PlanHash,
    string ChildOperationId,string CanonicalArtifactId,string ChildSha256,ZipAcknowledgementDetails? Details=null);

// Only a private quarantine implementation exposes this capability. No ordinary writes/reads/publication API.
public interface IZipPrivateContentStorage
{
    string NamespaceId { get; }
    long MaximumProtectedBytes { get; }
    Task StageAsync(ArtifactContentOperationId operation,ReadOnlyMemory<byte> candidate,CancellationToken ct=default);
    Task<byte[]?> ReadCandidateAsync(ArtifactContentOperationId operation,CancellationToken ct=default);
    Task<ArtifactContentMutationReceipt?> GetReceiptAsync(ArtifactContentOperationId operation,CancellationToken ct=default);
    Task<ArtifactContentMutationReceipt?> GetReceiptGuardedAsync(ArtifactContentOperationId operation,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP receipt inspection is required.");
    Task<IPreparedArtifactContentMutation> PrepareCreateAsync(ArtifactId id,ReadOnlyMemory<byte> candidate,ArtifactContentMutationContext context,CancellationToken ct=default);
    Task<IPreparedArtifactContentMutation> PrepareDeleteAsync(ArtifactId id,ArtifactContentRevision expected,ArtifactContentMutationContext context,CancellationToken ct=default);
    Task<IPreparedArtifactContentMutation> PrepareDeleteGuardedAsync(ArtifactId id,ArtifactContentRevision expected,
        ArtifactContentMutationContext context,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP protected deletion is required.");
    Task<IArtifactContentReadLease?> ReadProtectedAsync(ArtifactId id,ArtifactContentRevision expected,CancellationToken ct=default);
    Task<IArtifactContentReadLease?> ReadProtectedGuardedAsync(ArtifactId id,ArtifactContentRevision expected,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP protected inspection is required.");
    Task<ArtifactContentRevision?> ReadTombstoneRevisionGuardedAsync(ArtifactId id,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP tombstone inspection is required.");
    Task<IZipPreparedCandidateCleanup> PrepareCandidateCleanupAsync(ArtifactContentOperationId operation,string candidateHash,CancellationToken ct=default);
}
public interface IZipPreparedCandidateCleanup : IAsyncDisposable
{
    Task<ZipCandidateCleanupReceipt> ExecuteAsync(CancellationToken ct=default);
}
public interface IZipRetentionJournal
{
    Task<ZipRetentionRecord?> ReadRetentionAsync(string parentOperation,int fileOrdinal,CancellationToken ct=default);
    Task<ZipProtectedReleaseEvidence> ReadProtectedReleaseEvidenceAsync(string parentOperation,int fileOrdinal,CancellationToken ct=default)
        => throw new NotSupportedException("Protected release evidence is unavailable.");
    Task<ZipRetentionUpdate> ReserveRetentionAsync(ZipFence fence,int fileOrdinal,string namespaceId,string plaintextHash,CancellationToken ct=default);
    Task<ZipRetentionUpdate> ReserveRetentionAsync(ZipFence fence,int fileOrdinal,string namespaceId,string plaintextHash,IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipRetentionUpdate> BeginRetentionPreparationAsync(ZipFence fence,ZipRetentionRecord expected,CancellationToken ct=default);
    Task<ZipRetentionUpdate> BeginRetentionPreparationAsync(ZipFence fence,ZipRetentionRecord expected,IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipRetentionUpdate> BindRetentionCandidateAsync(ZipFence fence,ZipRetentionRecord expected,string hash,CancellationToken ct=default);
    Task<ZipRetentionUpdate> BindRetentionCandidateAsync(ZipFence fence,ZipRetentionRecord expected,string hash,IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipRetentionUpdate> PromoteRetentionAsync(ZipFence fence,ZipRetentionRecord expected,IPreparedArtifactContentMutation? prepared,
        ArtifactContentMutationReceipt? known,CancellationToken ct=default);
    Task<ZipRetentionUpdate> PromoteRetentionAsync(ZipFence fence,ZipRetentionRecord expected,IPreparedArtifactContentMutation? prepared,
        ArtifactContentMutationReceipt? known,IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipRetentionUpdate> ClaimRetentionReleaseAsync(ZipFence fence,ZipRetentionRecord expected,CancellationToken ct=default);
    Task<ZipRetentionUpdate> ClaimRetentionReleaseAsync(ZipProtectedReleaseAuthorityRequest request,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP release claim is required.");
    Task<ZipRetentionUpdate> PromoteRetentionReleaseAsync(ZipFence fence,ZipRetentionRecord expected,IPreparedArtifactContentMutation? prepared,
        ArtifactContentMutationReceipt? known,CancellationToken ct=default);
    Task<ZipRetentionUpdate> PromoteRetentionReleaseAsync(ZipProtectedReleaseAuthorityRequest request,
        IPreparedArtifactContentMutation? prepared,ArtifactContentMutationReceipt? known,IZipPrivateContentStorage storage,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP release receipt promotion is required.");
    Task<ZipRetentionUpdate> ObserveProtectedReleaseReceiptAsync(ZipProtectedReleaseAuthorityRequest request,
        IZipPrivateContentStorage storage,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP release receipt observation is required.");
    Task<ZipRetentionUpdate> FinishRetentionReleaseAsync(ZipFence fence,ZipRetentionRecord expected,IZipPreparedCandidateCleanup cleanup,CancellationToken ct=default);
    Task<ZipRetentionUpdate> ReviewRetentionAsync(ZipFence fence,ZipRetentionRecord expected,string safeReason,CancellationToken ct=default);
}
