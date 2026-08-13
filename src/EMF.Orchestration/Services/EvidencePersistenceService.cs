using EMF.Core.Contracts;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EvidencePersistenceService : IEvidencePersistenceService
{
    private readonly IEvidenceRepository _repository;

    public EvidencePersistenceService(
        IEvidenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public async Task PersistAsync(
        InventoryOrchestrationResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await _repository.AddArtifactWithProvenanceAsync(
            result.Artifact,
            result.Provenance,
            cancellationToken);
    }
}
