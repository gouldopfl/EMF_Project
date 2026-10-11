namespace EMF.Core.Contracts.Zip;

public interface IZipExtractionJournal
{
    Task InitializeAsync(CancellationToken ct = default);
    Task CreateAsync(ZipParentBinding binding, CancellationToken ct = default);
    Task CreateAsync(ZipParentBinding binding, IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipParentSnapshot?> ReadAsync(string operationId, CancellationToken ct = default);
    Task<ZipParentSnapshot> ClaimAsync(string operationId, string owner, TimeSpan duration, CancellationToken ct = default);
    Task<ZipParentSnapshot> AdmitPlanAsync(ZipFence fence, ZipPlan plan, CancellationToken ct = default);
    Task<ZipParentSnapshot> AdmitPlanAsync(ZipFence fence, ZipPlan plan, IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipParentSnapshot> ReserveAsync(ZipFence fence, ZipWorkReservation reservation, CancellationToken ct = default);
    Task<ZipParentSnapshot> ReserveAsync(ZipFence fence, ZipWorkReservation reservation, IZipEffectBoundaryGuard guard, CancellationToken ct = default) => throw new NotSupportedException("Guarded ZIP journal effects are required.");
    Task<ZipParentSnapshot> ReviewAsync(ZipFence fence, string safeReason, CancellationToken ct = default);
}
