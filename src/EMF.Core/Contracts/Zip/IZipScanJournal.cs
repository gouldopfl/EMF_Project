using EMF.Core.Contracts.Malware;
namespace EMF.Core.Contracts.Zip;
public sealed record ZipScanAttempt(string ReservationId,int FileOrdinal,string PlanHash,MalwareScanRequest Request);
public sealed record ZipScanUpdate(ZipParentSnapshot Parent,ZipScanAttempt Attempt);
public sealed record ZipFrozenScan(ZipScanAttempt? Attempt, MalwareScanEvidence? Evidence, int ChargedAttempts);
// Exact independently admitted observation. No execution/progression semantics.
public interface IZipScanObservationGuard : IZipEffectBoundaryGuard
{
    ZipParentAdmission Admission { get; }
    ZipParentBinding Parent { get; }
    ZipFence ExpectedFence { get; }
    ZipCompletedScanObservation Observation { get; }
}
public interface IZipScanJournal
{
    Task<ZipScanUpdate> BeginScanAsync(ZipFence fence,int fileOrdinal,MalwareScannerPolicy policy,CancellationToken ct=default);
    Task<ZipParentSnapshot> CompleteScanAsync(ZipFence fence,ZipScanAttempt attempt,MalwareScanEvidence evidence,CancellationToken ct=default);
    Task<ZipFrozenScan> ReadLatestScanAsync(ZipFence fence, int fileOrdinal, CancellationToken ct = default)
        => throw new NotSupportedException("Guarded scanner journal is unavailable.");
    Task<ZipScanUpdate> BeginScanAsync(ZipFence fence, int fileOrdinal, MalwareScannerPolicy policy, IZipEffectBoundaryGuard guard, CancellationToken ct = default)
        => throw new NotSupportedException("Guarded scanner reservation is unavailable.");
    Task<DateTimeOffset> ValidateScanReadAsync(ZipFence fence, ZipScanAttempt attempt, CancellationToken ct = default)
        => throw new NotSupportedException("Current scanner read eligibility is unavailable.");
    Task<ZipParentSnapshot> RecordScanObservationAsync(ZipFence fence, IZipScanObservationGuard observation, CancellationToken ct = default)
        => throw new NotSupportedException("Bounded scanner observation recording is unavailable.");
    Task<ZipParentSnapshot> AdvanceRecordedCleanAsync(ZipFence fence, ZipScanAttempt attempt, MalwareScanEvidence evidence, IZipEffectBoundaryGuard guard, CancellationToken ct = default)
        => throw new NotSupportedException("Guarded scanner progression is unavailable.");
}

public static class ZipScanOutcome
{
    public static bool IsTerminal(MalwareScanEvidence evidence,int attempts)
        => !evidence.IsClean && (evidence.Detection != MalwareDetection.Error || attempts >= ZipNumericLimits.Attempts ||
            evidence.Failure is not (MalwareScanFailure.TransportError or MalwareScanFailure.TimedOut or MalwareScanFailure.Cancelled));
}
