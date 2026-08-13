using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Integrity;
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


    public Task<Artifact?> FindArtifactAsync(
        string source,
        ContentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return _repository.FindArtifactAsync(
            source,
            fingerprint,
            cancellationToken);
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
