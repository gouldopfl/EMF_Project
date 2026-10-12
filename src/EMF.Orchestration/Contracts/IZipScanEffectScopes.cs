using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Contracts;

public sealed record ZipScanReservationRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent,
    ZipEntryPlan Occurrence, MalwareScannerPolicy Policy);
public sealed record ZipScanChildReadRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent,
    ZipScanAttempt Attempt, ZipRetentionRecord Retention);
public sealed record ZipScanProgressionRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent, ZipScanAttempt Attempt, MalwareScanEvidence Evidence);
public sealed record ZipScanCallRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent, ZipScannerInvocation Invocation, ZipScannerCall Call);
public sealed record ZipScanRecordingRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent, ZipScannerInvocation Invocation);

// Execution scope; expires/invalidates with root, with explicit ONE-call local
// admission semantics rather than the storage/journal effect contract.
public interface IZipAuthorizedScannerCallScope : IAsyncDisposable
{
    object Root { get; }
    ZipScanCallRequest Request { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
    CancellationToken Invalidated { get; }
    ValueTask RevalidateAsync(CancellationToken ct = default);
    IDisposable AcceptCall(CancellationToken ct = default);
}

// Independently admitted factual authority. This is NOT an execution scope.
// Execution revocation/cancellation/normal disposal does not revoke recording.
// Its own expiry/hard invalidation/disposal do. No takeover/progression/review.
public interface IZipScanObservationRecorder : IAsyncDisposable
{
    object Root { get; }
    ZipScanRecordingRequest Request { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
    CancellationToken Invalidated { get; }
    ValueTask RevalidateAsync(CancellationToken ct = default);
    IDisposable AcceptRecording(ZipCompletedScanObservation observation, ZipFence fence, CancellationToken ct = default);
}
