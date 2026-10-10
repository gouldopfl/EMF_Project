using EMF.Core.Contracts.Storage;
namespace EMF.Core.Contracts.Zip;
// The reserved -1 retention key belongs exclusively to the protected parent copy;
// file ordinals remain dense nonnegative occurrences. Existing ZIP-owned schema
// already supports this key and freezes the same receipt/candidate fields.
public sealed record ZipParentRetentionIdentity(string ParentOperationId,string ParentArtifactId,ZipRetainedBinding Source,string ProfileHash,
    string ObjectId,string CreateOperationId,string ReleaseOperationId,string OwnerToken,string NamespaceId);
public sealed record ZipParentRetentionRecord(ZipParentRetentionIdentity Identity,ZipRetentionState State=ZipRetentionState.Reserved,long Revision=1,
    string? CandidateHash=null,ArtifactContentMutationReceipt? CreateReceipt=null,ArtifactContentMutationReceipt? ReleaseReceipt=null,
    ZipCandidateCleanupReceipt? CandidateCleanup=null,string? SafeReason=null);
public sealed record ZipParentReleaseUpdate(ZipParentSnapshot Parent,ZipParentRetentionRecord Retention);
public interface IZipParentRetentionJournal
{
    Task<ZipParentRetentionRecord?> ReadParentRetentionAsync(string operation,CancellationToken ct=default);
    Task<ZipParentRetentionRecord> ReserveParentRetentionAsync(string operation,string parentArtifact,ZipRetainedBinding source,string profile,string namespaceId,CancellationToken ct=default);
    Task<ZipParentRetentionRecord> BeginParentPreparationAsync(ZipParentRetentionRecord expected,CancellationToken ct=default);
    Task<ZipParentRetentionRecord> BindParentCandidateAsync(ZipParentRetentionRecord expected,string hash,CancellationToken ct=default);
    Task<ZipParentRetentionRecord> PromoteParentRetentionAsync(ZipParentRetentionRecord expected,IPreparedArtifactContentMutation? prepared,ArtifactContentMutationReceipt? known,CancellationToken ct=default);
    Task<ZipParentRetentionRecord> ReviewParentCreationAsync(ZipParentRetentionRecord expected,string safeReason,CancellationToken ct=default);
    Task<ZipParentSnapshot> ReviewParentEvidenceAsync(ZipFence fence,string safeReason,CancellationToken ct=default);
    Task<ZipParentSnapshot> ClaimParentEvidenceReviewAsync(string operation,string owner,TimeSpan duration,CancellationToken ct=default);
    Task<ZipParentSnapshot> ChargeParentReadAsync(ZipFence fence,CancellationToken ct=default);
    Task<ZipParentSnapshot> CompleteParentAsync(ZipFence fence,CancellationToken ct=default);
    Task<ZipParentReleaseUpdate> ClaimParentReleaseAsync(ZipFence fence,ZipParentRetentionRecord expected,CancellationToken ct=default);
    Task<ZipParentReleaseUpdate> PromoteParentReleaseAsync(ZipFence fence,ZipParentRetentionRecord expected,IPreparedArtifactContentMutation? prepared,ArtifactContentMutationReceipt? known,CancellationToken ct=default);
    Task<ZipParentReleaseUpdate> FinishParentReleaseAsync(ZipFence fence,ZipParentRetentionRecord expected,IZipPreparedCandidateCleanup cleanup,CancellationToken ct=default);
    Task<ZipParentReleaseUpdate> ReviewParentRetentionAsync(ZipFence fence,ZipParentRetentionRecord expected,string safeReason,CancellationToken ct=default);
}
