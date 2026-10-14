using EMF.Core.Contracts.Storage;

namespace EMF.Core.Contracts.Zip;

// Historical facts only. No root grant, scope, acceptance ticket or revocation
// token belongs in these records. A future live slice must obtain fresh authority.
public sealed record ZipCandidateDirectoryIdentity(uint DeviceMajor,uint DeviceMinor,ulong Inode,ulong MountId);
public sealed record ZipCandidateFileIdentity(uint DeviceMajor,uint DeviceMinor,ulong Inode,
    ulong MountId,uint LinkCount,uint UserId,uint GroupId,ushort Mode,long Length,
    long ModifiedSeconds,uint ModifiedNanoseconds,long ChangedSeconds,uint ChangedNanoseconds);
public sealed record ZipCandidateCleanupHistory(string Actor,ZipOwnershipPurpose Purpose,
    string AttemptId,string InvocationId,ZipFence Fence,DateTimeOffset OwnerUntil,
    DateTimeOffset GrantValidUntil,string AdmissionDigest,string BootId,DateTimeOffset RecordedUtc);
public sealed record ZipCandidateCleanupAnchors(ZipParentBinding Parent,string PlanHash,
    ZipEntryPlan Occurrence,ZipRetentionIdentity Retention,ZipRetainedBinding ChildInput,
    ZipChildIngestionProof Ingestion,ZipAcknowledgementBinding Acknowledgement,
    long ContainsId,long DerivedFromId,string AdoptionDigest,string AuditFactsDigest,
    ArtifactContentMutationReceipt CreateReceipt,ArtifactContentMutationReceipt ReleaseReceipt);
public sealed record CandidateCleanupIntent(int ProtocolVersion,string CleanupOperationId,
    ZipCandidateCleanupAnchors Anchors,string CandidateHash,long StoredEncryptedLength,
    ZipCandidateDirectoryIdentity RootIdentity,ZipCandidateDirectoryIdentity SourceDirectoryIdentity,
    ZipCandidateFileIdentity SourceIdentity,string CustodyRelativePath,long BaseRetentionRevision,
    long CommittedRetentionRevision,ZipCandidateCleanupHistory InitiatingHistory,string IntentDigest);
public enum ZipCandidateCustodyObservation { MoveObservedInThisAttempt=1,RecoveryVerifiedRetainedObject=2 }
public sealed record CandidateCustodyCompletion(int ProtocolVersion,string CleanupOperationId,
    string IntentDigest,string NamespaceId,string ParentOperationId,int FileOrdinal,
    string CandidateHash,long StoredEncryptedLength,string CustodyRelativePath,
    ZipCandidateFileIdentity CustodyIdentity,ZipCandidateDirectoryIdentity CustodyDirectoryIdentity,
    bool ActiveSourceAbsent,bool CustodyObjectVerified,bool SourceDirectoryFlushed,
    bool CustodyDirectoryFlushed,ZipCandidateCustodyObservation Observation,
    string ReleaseReceiptDigest,long BaseRetentionRevision,long CommittedRetentionRevision,
    ZipCandidateCleanupHistory VerifyingHistory,string? EffectActor,string? EffectAttemptId,
    string CompletionDigest);

// Separate from the legacy three-field cleanup receipt. This is retained custody,
// not physical destruction, and no legacy AlreadyAbsent receipt is D1 evidence.
public sealed record ZipCandidateCustodyReceipt(int ProtocolVersion,string CleanupOperationId,
    string IntentDigest,string CompletionDigest,string NamespaceId,string CustodyRelativePath,
    string CandidateHash,long StoredEncryptedLength);
