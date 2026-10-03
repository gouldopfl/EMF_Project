using System.Text.Json.Serialization;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Ingestion;

public readonly record struct IngestionClassificationId
{
    public string Value { get; }
    [JsonConstructor] public IngestionClassificationId(string value) => Value = ArtifactContentIdentity.Validate(value);
}
public readonly record struct IngestionClassificationRevision
{
    public string Value { get; }
    [JsonConstructor] public IngestionClassificationRevision(string value) => Value = ArtifactContentIdentity.Validate(value);
    public static IngestionClassificationRevision New() => new(Guid.NewGuid().ToString("N"));
}
public readonly record struct IngestionAuthorizedOperationId
{
    public string Value { get; }
    [JsonConstructor] public IngestionAuthorizedOperationId(string value) => Value = ArtifactContentIdentity.Validate(value);
}
public enum ArtifactIngestionState { Prepared, ContentCreated, MetadataCommitted, Completed, CleanupClaimed, Cleaned, RequiresReview }
public enum ArtifactIngestionDisposition { None, ProvisionalArtifactAdopted, DeduplicatedToCanonicalArtifact, Conflict }
public enum IngestionAuditAction { Created, Adopted, Deduplicated, CleanupClaimed, Deleted, NoContent, RequiresReview, Reconciled, CreationRejected, CleanupRejected }
public enum IngestionAuditDelivery { Pending, Completed, RequiresReview }
public enum IngestionFailureCategory
{
    AdoptionClassificationMismatch, AuditEvidenceFailure, AuditIdentityConflict, AuditJournalDamage,
    CandidateIntegrityFailure, CanonicalReconciliationFailure, CleanupAuthorityFailure, CleanupAuthorizationUnavailable,
    CleanupOutcomeUnknown, CleanupReceiptConflict, ContradictoryAdoption, CreationEvidenceFailure,
    CreationOutcomeUnknown, CreationReceiptConflict, DeliveryRecoveryPending, LifecycleDamage,
    ProvisionalAuthorityFailure, ReceiptAuditConflict, ReceiptWithoutValidIntent, RecoveryAuthorizationDenied,
    RecoveryEvidenceFailure, UnexpectedPhysicalContent
}

// Coordination only. Evidence and source information belongs in the governed draft/metadata boundary.
public sealed record ArtifactIngestionIntent(
    ArtifactContentOperationId OperationId, ArtifactId ArtifactId, ArtifactContentOwnershipToken OwnershipToken,
    IngestionClassificationId ClassificationId, IngestionClassificationRevision ClassificationRevision,
    IngestionAuthorizedOperationId AuthorizedOperationId, ArtifactContentOperationId CleanupOperationId,
    ArtifactContentAuditEventId CreateEventId, ArtifactContentAuditEventId AdoptionEventId, ArtifactContentAuditEventId CleanupEventId,
    DateTimeOffset PreparedUtc, long Revision = 1, ArtifactIngestionState State = ArtifactIngestionState.Prepared,
    string? CandidateHash = null, ArtifactContentMutationReceipt? CreateReceipt = null,
    ArtifactContentMutationReceipt? CleanupReceipt = null, ArtifactIngestionDisposition Disposition = ArtifactIngestionDisposition.None,
    ArtifactId? CanonicalArtifactId = null, string? CleanupActorId = null, string? SafeFailureCategory = null,
    DateTimeOffset? AdoptionUtc = null, IngestionAuditObligation? RecoveryAuditObligation = null);

public sealed record IngestionOperationBinding(IngestionAuthorizedOperationId OperationId, string OriginalActorId);
public sealed record IngestionClassificationAuthority(ArtifactId ArtifactId, IngestionClassificationId ClassificationId,
    IngestionClassificationRevision Revision, bool IsAdopted, ArtifactContentOwnershipToken? OwnershipToken,
    IngestionAuthorizedOperationId? AuthorizedOperationId);
public sealed record IngestionMetadataDraft(Artifact Artifact, Provenance Provenance);
public sealed record IngestionAuditObligation(ArtifactContentAuditEventId EventId, ArtifactContentOperationId OperationId,
    ArtifactId ArtifactId, IngestionClassificationId ClassificationId, IngestionClassificationRevision ClassificationRevision,
    string OriginalActorId, string ExecutingActorId, bool IsRecovery, IngestionAuditAction Action,
    DateTimeOffset OccurredUtc, string? SafeFailureCategory = null, ArtifactContentOperationId? MutationOperationId = null,
    int SchemaVersion = 1);
public sealed record IngestionRecoveryWork(long Cursor, ArtifactContentOperationId OperationId);
public sealed record IngestionRecoveryHealth(long PendingIntentCount, long PendingAuditCount, long RequiresReviewCount,
    long OrphanReceiptCount, DateTimeOffset ObservedUtc);
public sealed record ArtifactIngestionOutcome(ArtifactContentOperationId OperationId, ArtifactIngestionState State,
    ArtifactIngestionDisposition Disposition, IngestionMetadataDraft? Result, bool IsAdopted,
    IngestionAuditDelivery AuditDelivery, string? SafeFailureCategory = null);

public sealed class ArtifactIngestionReviewException : InvalidOperationException
{
    public ArtifactContentOperationId OperationId { get; }
    public ArtifactIngestionReviewException(ArtifactContentOperationId operationId)
        : base("Artifact ingestion requires review; durable lifecycle work is retained.") => OperationId = operationId;
}
