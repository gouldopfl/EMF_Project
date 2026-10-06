using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Contracts;

// Replay is read-only recognition of the actual durable row, never an ownership grant.
public interface IReviewerOperationSnapshotRepository
{
    Task<ReviewerOperationSnapshotRecord?> ReadAsync(OperationSnapshotId snapshotId,
        CancellationToken cancellationToken = default);
    Task<ReviewerOperationSnapshotRecord?> ReadByReviewerOperationAsync(ReviewerOperationId operationId,
        CancellationToken cancellationToken = default);
    Task<ReviewerOperationSnapshotRecord> CreateCapturingAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerSnapshotOwnerToken ownerToken,
        string profile = ReviewerRetainedValidator.Profile, int representationVersion = 1,
        CancellationToken cancellationToken = default);
    Task<ReviewerOperationSnapshotRecord> BindAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerSnapshotCandidate candidate, CancellationToken cancellationToken = default);
    Task<ReviewerOperationSnapshotRecord> ReadyAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerSnapshotReadyEvidence evidence, CancellationToken cancellationToken = default);
    Task<ReviewerOperationSnapshotRecord> TransferOwnershipAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerSnapshotOwnerToken replacementToken, CancellationToken cancellationToken = default);
    Task<ReviewerOperationSnapshotRecord> RecordReviewOrFailureAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerOperationSnapshotState expectedState,
        long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerOperationSnapshotFailureCategory category, CancellationToken cancellationToken = default);
}
