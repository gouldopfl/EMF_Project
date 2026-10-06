using EMF.Core.Contracts.Storage;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public readonly record struct ReviewerOperationId
{
    public string Value { get; }
    public ReviewerOperationId(string value) => Value = ArtifactContentIdentity.Validate(value);
    public static ReviewerOperationId New() => new(Guid.NewGuid().ToString("N"));
}

public readonly record struct ReviewerSnapshotOwnerToken
{
    public string Value { get; }
    public ReviewerSnapshotOwnerToken(string value) => Value = ArtifactContentIdentity.Validate(value);
    public static ReviewerSnapshotOwnerToken New() => new(Guid.NewGuid().ToString("N"));
}

public enum ReviewerOperationSnapshotState { Capturing, Materializing, Ready }
public enum ReviewerOperationSnapshotDisposition { Active, RequiresReview, Failed }
public enum ReviewerOperationSnapshotFailureCategory
{
    CaptureRecoveryAmbiguous = 1,
    CaptureRejected = 2,
    RetainedMaterialInvalid = 3
}

// This version identifies retained proof-profile validation, not reviewer assembly.
public static class ReviewerRetainedValidationContract
{
    public const int Version = 1;
}

public sealed record ReviewerSnapshotCandidate(ReviewerRetainedReference Reference,
    string Profile, int RepresentationVersion);

// Persistence binds this claim; the Stage 2B2 coordinator must perform validation.
public sealed record ReviewerSnapshotReadyEvidence(ReviewerSnapshotCandidate Candidate,
    int ValidationVersion);

public sealed record ReviewerOperationSnapshotRecord(OperationSnapshotId SnapshotId,
    ReviewerOperationId ReviewerOperationId, ReviewerOperationSnapshotState State,
    long Revision, ReviewerSnapshotOwnerToken OwnerToken, string Profile,
    int RepresentationVersion, string? BundleSha256, int? ReadyValidationVersion,
    ReviewerOperationSnapshotDisposition Disposition,
    ReviewerOperationSnapshotFailureCategory? FailureCategory)
{
    // Lifecycle/disposition authority only. Consumers must additionally validate
    // retained material and current authorization; this proof profile does not
    // authorize full reviewer assembly.
    public bool IsConsumable => State == ReviewerOperationSnapshotState.Ready &&
        Disposition == ReviewerOperationSnapshotDisposition.Active;
}

public sealed class ReviewerOperationSnapshotConflictException(string message)
    : InvalidOperationException(message);

public sealed class ReviewerOperationSnapshotConcurrencyException(string message)
    : InvalidOperationException(message);
