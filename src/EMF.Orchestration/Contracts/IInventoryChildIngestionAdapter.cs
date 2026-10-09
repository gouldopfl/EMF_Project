using EMF.Inventory.Models;

namespace EMF.Orchestration.Contracts;

// Supplied by an authenticated host, never inferred from discovery or command-line input.
public interface IInventoryChildIngestionAdapter
{
    Task<InventoryAuthorityBinding> ValidateAuthorityAsync(InventoryAuthorityBinding? expected, CancellationToken ct);
    Task<InventoryChildConfirmation> ExecuteAsync(InventoryParentPlan parent, InventoryPlanItem child,
        Func<CancellationToken, Task<byte[]>> readContent, CancellationToken ct);
}

public sealed class InventoryChildRejectedException(string message) : InvalidOperationException(message);
