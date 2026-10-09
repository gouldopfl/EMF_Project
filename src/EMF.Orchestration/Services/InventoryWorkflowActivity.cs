using System.Diagnostics;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Discovery.Models;
using EMF.Inventory.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryWorkflowActivity : IWorkflowActivity
{
    private readonly IInventoryOrchestrationService _service;
    private readonly IEvidencePersistenceService _persistence;
    private readonly string _sourcePath;
    private readonly DiscoveryOptions _options;
    private readonly InventoryMode _mode;
    private readonly IInventoryChildIngestionAdapter? _child;
    private readonly Func<string, CancellationToken, Task>? _checkpoint;
    public InventoryWorkflowActivity(IInventoryOrchestrationService service, IEvidencePersistenceService persistence,
        IContentFingerprintService fingerprintService, IArtifactContentStore? contentStore, string sourcePath, DiscoveryOptions options)
        : this(service, persistence, sourcePath, options, InventoryMode.MetadataOnly)
    {
        ArgumentNullException.ThrowIfNull(fingerprintService);
        if (contentStore is not null) throw new NotSupportedException("Inventory publication requires an authenticated child ingestion capability.");
    }
    public InventoryWorkflowActivity(IInventoryOrchestrationService service, IEvidencePersistenceService persistence,
        string sourcePath, DiscoveryOptions options, InventoryMode mode = InventoryMode.MetadataOnly,
        IInventoryChildIngestionAdapter? child = null, Func<string, CancellationToken, Task>? checkpoint = null)
    {
        ArgumentNullException.ThrowIfNull(service); ArgumentNullException.ThrowIfNull(persistence);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath); ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == InventoryMode.ProtectedContent && child is null) throw new UnauthorizedAccessException("Authoritative Inventory ingestion capability is unavailable.");
        (_service, _persistence, _sourcePath, _options, _mode, _child, _checkpoint) = (service, persistence, sourcePath, options, mode, child, checkpoint);
    }
    public string Id => "inventory";
    public string Name => "Inventory";
    private Task Checkpoint(string stage, CancellationToken ct) => _checkpoint?.Invoke(stage, ct) ?? Task.CompletedTask;
    public async Task<WorkflowActivityResult> ExecuteAsync(WorkflowExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_service is not InventoryOrchestrationService runtime)
            throw new NotSupportedException("Inventory workflow requires the durable parent/retained-input composition.");
        var operation = context.OperationId ?? throw new InvalidOperationException("Durable Inventory requires a workflow operation identity.");
        var ct = cancellationToken; var watch = Stopwatch.StartNew();
        await using var gate = await runtime.AcquireAsync(operation.Value, ct).ConfigureAwait(false);
        var loaded = await runtime.Journal.LoadAsync(operation.Value, ct).ConfigureAwait(false);
        var authority = _mode == InventoryMode.ProtectedContent ? await _child!.ValidateAuthorityAsync(loaded?.Plan.Authority, ct).ConfigureAwait(false) : null;
        var state = await runtime.AdmitOrLoadAsync(context.WorkflowId.Value, operation.Value, _sourcePath, _options, _mode, authority, ct).ConfigureAwait(false);
        if (state.Status == InventoryParentStatus.RequiresReview) throw new InvalidOperationException("Inventory parent requires review.");
        if (loaded is not null && state.Status == InventoryParentStatus.Active)
            state = await runtime.Journal.TakeOwnershipAsync(state, InventoryIdentity.New(), ct).ConfigureAwait(false);
        // A confirmation can survive a crash before retained release. Reconcile that release
        // without materializing or reprocessing already confirmed content.
        for (var ordinal = 0; ordinal <= state.ConfirmedOrdinal; ordinal++)
        {
            await runtime.Journal.AuthorizeReleaseAsync(state, ordinal, ct).ConfigureAwait(false);
            await runtime.Snapshots.ReleaseAsync(state.Plan.Items[ordinal].Retained, ct).ConfigureAwait(false);
        }
        while (state.ConfirmedOrdinal + 1 < state.Plan.Items.Count)
        {
            var item = state.Plan.Items[state.ConfirmedOrdinal + 1];
            if (_mode == InventoryMode.ProtectedContent) await _child!.ValidateAuthorityAsync(state.Plan.Authority, ct).ConfigureAwait(false);
            await Checkpoint("BeforeChildStart", ct).ConfigureAwait(false);
            await runtime.Journal.StartNextAsync(state, item.Ordinal, ct).ConfigureAwait(false);
            try
            {
                InventoryChildConfirmation confirmation;
                if (_mode == InventoryMode.MetadataOnly)
                {
                    var result = await runtime.ReadAsync(item, ct).ConfigureAwait(false);
                    var fingerprint = result.Artifact.Fingerprint ?? throw new InvalidDataException("Missing admitted fingerprint.");
                    var canonical = await _persistence.FindArtifactAsync(item.SourceLocator, fingerprint, ct).ConfigureAwait(false);
                    if (canonical is null)
                    {
                        await _persistence.PersistAsync(result, ct).ConfigureAwait(false);
                        canonical = await _persistence.FindArtifactAsync(item.SourceLocator, fingerprint, ct).ConfigureAwait(false)
                            ?? throw new InvalidDataException("Metadata persistence has no durable confirmation.");
                    }
                    confirmation = new(item.Ordinal, item.ChildOperationId, InventoryConfirmationDisposition.MetadataPersisted, canonical.Id.Value,
                        $"metadata:{canonical.Id.Value}:{item.Retained.Fingerprint}");
                }
                else confirmation = await _child!.ExecuteAsync(state.Plan, item, async token => { await runtime.ReadAsync(item, token).ConfigureAwait(false); return await runtime.ReadContentAsync(item, token).ConfigureAwait(false); }, ct).ConfigureAwait(false);
                await Checkpoint("AfterChildEffect", ct).ConfigureAwait(false);
                state = await runtime.Journal.ConfirmNextAsync(state, confirmation, ct).ConfigureAwait(false);
                runtime.Confirmed();
                await Checkpoint("AfterParentConfirmation", ct).ConfigureAwait(false);
                await runtime.Journal.AuthorizeReleaseAsync(state, item.Ordinal, ct).ConfigureAwait(false);
                await runtime.Snapshots.ReleaseAsync(item.Retained, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure)
            {
                runtime.Failed();
                // Confirmation may already be durable. Never turn an adopted/confirmed child
                // into a retryable failure or remove its retained object speculatively.
                if (state.ConfirmedOrdinal < item.Ordinal)
                    await runtime.Journal.MarkChildFailureAsync(state, item.Ordinal, failure is InventoryChildRejectedException or EMF.Core.Contracts.Ingestion.ArtifactIngestionReviewException or InvalidDataException or UnauthorizedAccessException, ct).ConfigureAwait(false);
                throw;
            }
        }
        if (state.Status == InventoryParentStatus.Active) await runtime.Journal.CompleteAsync(state, ct).ConfigureAwait(false);
        runtime.Elapsed(watch.Elapsed);
        return new() { Succeeded = true, Message = $"Inventory confirmed {state.Plan.Items.Count} item(s).", CompletedUtc = DateTimeOffset.UtcNow };
    }
}
