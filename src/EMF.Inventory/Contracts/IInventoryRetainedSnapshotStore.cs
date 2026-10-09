using EMF.Inventory.Models;

namespace EMF.Inventory.Contracts;

public interface IInventoryRetainedSnapshotStore
{
    Task<InventoryRetainedBinding> CaptureAsync(string parentId, string sourcePath, CancellationToken ct = default);
    Task<InventorySnapshotLease> MaterializeAsync(InventoryRetainedBinding binding, CancellationToken ct = default);
    Task ReleaseAsync(InventoryRetainedBinding binding, CancellationToken ct = default);
}

public interface IInventoryProtectedSnapshotStorage
{
    Task<InventoryRetainedBinding> SealAsync(InventoryRetentionRecord request, string plaintextPath, CancellationToken ct = default);
    Task MaterializeAsync(InventoryRetainedBinding binding, string destination, CancellationToken ct = default);
    Task ReleaseAsync(InventoryRetainedBinding binding, CancellationToken ct = default);
}

// Compatibility inspection has no durable operation identity or publication effect.
// Its local plaintext snapshot is owned exclusively by the returned lease.
public interface IInventoryEphemeralSnapshotStore
{
    Task<InventorySnapshotLease> CaptureEphemeralAsync(string sourcePath, CancellationToken ct = default);
}
