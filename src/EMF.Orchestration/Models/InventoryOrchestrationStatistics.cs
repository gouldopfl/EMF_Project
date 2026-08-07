namespace EMF.Orchestration.Models;

public sealed class InventoryOrchestrationStatistics
{
    public int ItemsDiscovered { get; set; }

    public int ItemsHandled { get; set; }

    public int ItemsSkipped { get; set; }

    public int InventoriesCompleted { get; set; }

    public int ItemsFailed { get; set; }

    public TimeSpan Elapsed { get; set; }
}
