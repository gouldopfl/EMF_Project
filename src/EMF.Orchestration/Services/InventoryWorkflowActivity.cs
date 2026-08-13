using EMF.Core.Contracts.Storage;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryWorkflowActivity : IWorkflowActivity
{
    private readonly IInventoryOrchestrationService _service;
    private readonly IEvidencePersistenceService _persistence;
    private readonly IArtifactContentStore? _contentStore;
    private readonly string _sourcePath;
    private readonly DiscoveryOptions _options;

    public InventoryWorkflowActivity(
        IInventoryOrchestrationService service,
        IEvidencePersistenceService persistence,
        IArtifactContentStore? contentStore,
        string sourcePath,
        DiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        _service = service;
        _persistence = persistence;
        _contentStore = contentStore;
        _sourcePath = sourcePath;
        _options = options;
    }

    public string Id => "inventory";

    public string Name => "Inventory";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var total = 0;
        var failed = 0;

        await foreach (var result in _service.ExecuteAsync(
            _sourcePath,
            _options,
            cancellationToken))
        {
            total++;

            if (!result.Success)
            {
                failed++;
                continue;
            }

            if (result.Artifact.Fingerprint is not null)
            {
                var existing =
                    await _persistence.FindArtifactAsync(
                        result.Provenance.Source,
                        result.Artifact.Fingerprint,
                        cancellationToken);

                if (existing is not null)
                {
                    if (_contentStore is not null)
                    {
                        await _contentStore.DeleteAsync(
                            result.Artifact.Id,
                            cancellationToken);
                    }

                    continue;
                }
            }

            try
            {
                await _persistence.PersistAsync(
                    result,
                    cancellationToken);
            }
            catch (Exception persistenceException)
                when (_contentStore is not null &&
                      persistenceException is not OperationCanceledException)
            {
                try
                {
                    await _contentStore.DeleteAsync(
                        result.Artifact.Id,
                        cancellationToken);
                }
                catch (Exception cleanupException)
                    when (cleanupException is not OperationCanceledException)
                {
                    throw new AggregateException(
                        "Evidence persistence failed and artifact content cleanup also failed.",
                        persistenceException,
                        cleanupException);
                }

                throw;
            }
        }

        return new WorkflowActivityResult
        {
            Succeeded = failed == 0,
            Message = $"Inventory processed {total} item(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
