using EMF.Discovery.Models;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IInventoryOrchestrationService
{
    IAsyncEnumerable<InventoryOrchestrationResult> ExecuteAsync(
        string sourcePath,
        DiscoveryOptions options,
        CancellationToken cancellationToken = default);
}
