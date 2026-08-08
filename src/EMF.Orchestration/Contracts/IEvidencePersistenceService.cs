using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IEvidencePersistenceService
{
    Task PersistAsync(
        InventoryOrchestrationResult result,
        CancellationToken cancellationToken = default);
}
