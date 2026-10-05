using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Ingestion;

// A provider-neutral fenced metadata session. Its implementation serializes adoption,
// cleanup claims and classification writers across processes. Dispose without Commit rolls back.
public interface IArtifactIngestionPersistence
{
    // Execution coordination is operation-specific and must be acquired before metadata sessions.
    // It excludes competing producers/recovery, but carries no authorization or recovery truth.
    Task<IDisposable> AcquireExecutionAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default);
    Task<IArtifactIngestionSession> AcquireAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IngestionRecoveryWork>> ReadRecoveryWorkAsync(long afterCursor, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IngestionAuditObligation>> ReadAuditObligationsAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default);
    // Independent immutable event facts; never infer acknowledgement from outbox state.
    Task<IngestionAuditObligation?> ReadReceiptAuditObligationAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default);
    Task<IngestionAuditObligation?> ReadReviewAuditObligationAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default);
    Task AcknowledgeAuditAsync(IngestionAuditObligation expected, CancellationToken cancellationToken = default);
    Task MarkAuditPendingAsync(IngestionAuditObligation expected, CancellationToken cancellationToken = default);
    Task RecordOrphanReceiptAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default);
    Task<ArtifactContentOperationId?> FindReceiptOperationAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default);
    Task<IngestionRecoveryHealth> ReadRecoveryHealthAsync(CancellationToken cancellationToken = default);
}
public interface IArtifactIngestionSession : IAsyncDisposable
{
    ArtifactContentOperationId OperationId { get; }
    ArtifactIngestionIntent? Intent { get; }
    IngestionOperationBinding? OperationBinding { get; }
    ArtifactId? ProvisionalArtifactId { get; }
    bool IsDamaged { get; }
    bool HasReview { get; }
    bool CandidatePreparationStarted { get; }
    IngestionCandidateBinding? CandidateBinding { get; }
    ArtifactContentMutationReceipt? CandidateCreationReceipt { get; }
    Task<bool> HasAdoptionEvidenceAsync(ArtifactId artifactId, CancellationToken cancellationToken = default);
    Task<IngestionClassificationAuthority?> ResolveAuthorityAsync(ArtifactId artifactId, CancellationToken cancellationToken = default);
    Task<IngestionMetadataDraft?> ReadDraftAsync(CancellationToken cancellationToken = default);
    Task<IngestionMetadataDraft?> FindCanonicalAsync(CancellationToken cancellationToken = default);
    Task<IngestionMetadataDraft?> ReadResultAsync(CancellationToken cancellationToken = default);
    Task PrepareAsync(ArtifactIngestionIntent intent, IngestionOperationBinding binding, IngestionMetadataDraft draft, CancellationToken cancellationToken = default);
    Task BeginCandidatePreparationAsync(ArtifactIngestionIntent expected, CancellationToken cancellationToken = default);
    Task SetCandidateAsync(ArtifactIngestionIntent expected, string candidateHash, CancellationToken cancellationToken = default);
    Task RecordCreatedAsync(ArtifactIngestionIntent expected, ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default);
    Task AdoptAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority provisional,
        IngestionClassificationAuthority? canonical, string? cleanupActorId, CancellationToken cancellationToken = default);
    Task ClaimCleanupAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority, string recoveryActorId, CancellationToken cancellationToken = default);
    Task RecordDeduplicationConflictAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority,
        ArtifactId canonicalArtifactId, string? cleanupActorId, CancellationToken cancellationToken = default);
    Task RecordCleanedAsync(ArtifactIngestionIntent expected, ArtifactContentMutationReceipt? receipt, string recoveryActorId, CancellationToken cancellationToken = default);
    Task RecordReviewAsync(string safeFailureCategory, string executingActorId, CancellationToken cancellationToken = default, bool isRecovery = true);
    Task EnsureReceiptAuditAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default);
    Task EnsureAdoptionAuditAsync(CancellationToken cancellationToken = default);
    Task RecordRecoveryCompletionAsync(string recoveryActorId, CancellationToken cancellationToken = default);
    Task CompleteAsync(ArtifactIngestionIntent expected, CancellationToken cancellationToken = default);
    Task ReopenAuditDeliveryAsync(ArtifactIngestionIntent expected, CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
}
public interface IArtifactIngestionCoordinator
{
    Task<ArtifactIngestionOutcome> IngestAsync(IngestionMetadataDraft draft, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);
    // Resume the authenticated original operation using retained inputs only; no source path/plaintext request.
    Task<ArtifactIngestionOutcome> ResumeAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Retained-input ingestion resume is unsupported.");
    Task<ArtifactIngestionOutcome?> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
}
