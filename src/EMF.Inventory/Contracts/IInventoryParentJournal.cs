using EMF.Inventory.Models;

namespace EMF.Inventory.Contracts;

public interface IInventoryParentJournal
{
    Task<InventoryParentState> AdmitAsync(InventoryParentPlan plan, string ownerToken, CancellationToken ct = default);
    Task<InventoryParentState?> LoadAsync(string parentId, CancellationToken ct = default);
    Task<InventoryParentState> TakeOwnershipAsync(InventoryParentState expected, string ownerToken, CancellationToken ct = default);
    Task StartNextAsync(InventoryParentState expected, int ordinal, CancellationToken ct = default);
    Task<InventoryParentState> ConfirmNextAsync(InventoryParentState expected, InventoryChildConfirmation confirmation, CancellationToken ct = default);
    Task<InventoryParentState> CompleteAsync(InventoryParentState expected, CancellationToken ct = default);
    Task MarkChildFailureAsync(InventoryParentState expected, int ordinal, bool terminal, CancellationToken ct = default);
    Task<bool> HasRetainedPreparationAsync(string parentId, CancellationToken ct = default);
    Task ReserveRetentionAsync(InventoryRetentionRecord record, CancellationToken ct = default);
    Task<InventoryRetentionRecord?> ReadRetentionAsync(string objectId, CancellationToken ct = default);
    Task UpdateRetentionAsync(InventoryRetentionRecord expected, InventoryRetentionRecord updated, CancellationToken ct = default);
    Task AuthorizeReleaseAsync(InventoryParentState expected, int ordinal, CancellationToken ct = default);
}
