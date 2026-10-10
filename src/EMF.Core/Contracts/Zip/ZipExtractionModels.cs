namespace EMF.Core.Contracts.Zip;

public enum ZipParentState { AdmissionPending, Planned, Processing, RequiresReview, Completed, Released }
public enum ZipEntryState { Planned, Materialized, Scanned, Ingested, Acknowledged, RequiresReview, Released }
public enum ZipWorkKind { Preflight, Extraction, Scanner, Ingestion }

public sealed record ZipRetainedBinding(string ContentId, string Revision, string Sha256, long Length);
public sealed record ZipParentBinding(string OperationId, string ParentArtifactId, ZipRetainedBinding Input, string ProfileHash);
public sealed record ZipFence(string OperationId, string Owner, long Epoch, long Revision, string? PlanHash, int ConfirmedOrdinal);
public sealed record ZipEntryPlan(int CentralOrdinal, int? FileOrdinal, string FullName, string DisplayName,
    long CompressedLength, long ExpandedLength, uint Crc32, bool IsEncrypted, bool IsDirectory,
    string? ChildOperationId, string? ProvisionalArtifactId);
public sealed record ZipPlan(string Hash, string PreflightReceiptJson, IReadOnlyList<ZipEntryPlan> Entries);
public sealed record ZipEntryProgress(ZipEntryPlan Plan, ZipEntryState State = ZipEntryState.Planned,
    ZipRetainedBinding? Retained = null, string? EvidenceJson = null, string? SafeReason = null, ZipChildIngestionProof? Ingestion = null);
public sealed record ZipBudget(long PreflightAttempts = 0, long PreflightBytes = 0,
    long ExtractionAttempts = 0, long ExpandedReserved = 0, long ExpandedProduced = 0,
    long ReplayExpanded = 0, long CompressedReserved = 0, long ParentReadBytes = 0, long ProbeBytes = 0,
    long CrcBytes = 0, long ScannerAttempts = 0, long ScannerReserved = 0, long ReplayScanner = 0,
    long IngestionAttempts = 0, long RejectedEntries = 0);
public sealed record ZipWorkReservation(string Id, ZipWorkKind Kind, int? FileOrdinal,
    long ExpandedBytes = 0, long CompressedBytes = 0);
public sealed record ZipParentSnapshot(ZipParentBinding Binding, ZipFence Fence, DateTimeOffset OwnerUntil,
    ZipParentState State, ZipPlan? Plan, ZipBudget Budget, IReadOnlyList<ZipEntryProgress> Entries);

public sealed class ZipFenceException() : InvalidOperationException("ZIP ownership, revision, plan or frontier changed.");

// These are represented-byte/work limits, not a process-memory or CPU guarantee.
public static class ZipNumericLimits
{
    public const long Parent = 33_554_432, Child = 52_428_800, Aggregate = 104_857_600;
    public const int Entries = 1000, Attempts = 2;
    public const long ExpandedWork = 209_715_200, CompressedWork = 67_108_864, ParentReads = 134_217_728;
    public const long ScannerStreamMinimum = 52_432_014, ScannerWork = 134_217_728;
    public const int ScannerChunk = 65_536;
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(120);
}
