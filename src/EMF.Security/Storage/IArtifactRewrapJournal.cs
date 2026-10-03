using EMF.Core.Models.Identities;
using EMF.Core.Contracts.Storage;
using EMF.Security.Auditing.Models;
using EMF.Security.Models.Identities;
namespace EMF.Security.Storage;

public enum ArtifactRewrapState { Authorized, Prepared, CommittedAuditPending, Completed, RequiresReview }
public sealed record ArtifactRewrapIntent(
    SecurityMutationOperationId OperationId, SecurityAuditEventId AuditEventId, ArtifactId ArtifactId,
    string OriginalActorId, ProtectionClassificationId ClassificationId, ArtifactClassificationRevision ClassificationRevision,
    ArtifactContentRevision ExpectedRevision, DateTimeOffset AuthorizedUtc,
    ArtifactRewrapState State = ArtifactRewrapState.Authorized, string? CandidateHash = null,
    string? PreviousKeyId = null, string? CurrentKeyId = null,
    ArtifactContentMutationReceipt? Receipt = null, SecurityAuditRecord? Event = null);
public sealed record ArtifactRewrapRecoveryWork(SecurityMutationOperationId OperationId, SecurityAuditEventId AuditEventId,
    ArtifactId ArtifactId, ArtifactRewrapState State, string SafeFailureCategory, SecurityAuditRecord? RecoveryEvent = null);
public interface IArtifactRewrapJournal
{
    Task<ArtifactRewrapIntent?> ReadAsync(SecurityMutationOperationId operationId, CancellationToken cancellationToken = default);
    Task CreateAsync(ArtifactRewrapIntent intent, CancellationToken cancellationToken = default);
    Task UpdateAsync(ArtifactRewrapIntent expected, ArtifactRewrapIntent updated, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtifactRewrapIntent>> ReadPendingAsync(int limit, CancellationToken cancellationToken = default);
    Task<ArtifactRewrapRecoveryWork?> ReadReviewAsync(SecurityMutationOperationId operationId, CancellationToken cancellationToken = default);
    Task RecordReviewAsync(ArtifactRewrapRecoveryWork work, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtifactRewrapRecoveryWork>> ReadRecoveryWorkAsync(int limit, CancellationToken cancellationToken = default);
}
