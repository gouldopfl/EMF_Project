using EMF.Core.Models;
using EMF.Core.Models.Integrity;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IEvidencePersistenceService
{
    Task PersistAsync(
        InventoryOrchestrationResult result,
        CancellationToken cancellationToken = default);

    Task<Artifact?> FindArtifactAsync(
        string source,
        ContentFingerprint fingerprint,
        CancellationToken cancellationToken = default);
}
