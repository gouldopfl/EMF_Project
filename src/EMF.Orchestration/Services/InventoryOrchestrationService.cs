using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Inventory.Contracts;
using EMF.Inventory.Models;
using EMF.Inventory.Storage;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryOrchestrationService : IInventoryOrchestrationService
{
    private readonly IStreamingDiscoveryService _discovery;
    private readonly IInventoryRoutingService _routing;
    private readonly IArtifactFactory _factory;
    private readonly IArtifactIdGenerator _ids;
    private readonly IContentFingerprintService _fingerprints;
    private readonly IInventoryRetainedSnapshotStore? _snapshots;
    private readonly IInventoryParentJournal? _journal;
    private readonly LinuxInventorySnapshotWorkspace? _workspace;
    private readonly InventoryProcessingLimits _limits;
    public InventoryOrchestrationStatistics Statistics { get; private set; } = new();
    public InventoryOrchestrationService(IStreamingDiscoveryService discovery, IInventoryRoutingService routing, IArtifactFactory artifactFactory,
        IArtifactIdGenerator artifactIdGenerator, IContentFingerprintService fingerprintService, IArtifactContentStore? contentStore = null)
    {
        ArgumentNullException.ThrowIfNull(discovery); ArgumentNullException.ThrowIfNull(routing); ArgumentNullException.ThrowIfNull(artifactFactory); ArgumentNullException.ThrowIfNull(artifactIdGenerator); ArgumentNullException.ThrowIfNull(fingerprintService);
        if (contentStore is not null) throw new NotSupportedException("Inventory content publication requires the authenticated child adapter.");
        (_discovery, _routing, _factory, _ids, _fingerprints, _limits) = (discovery, routing, artifactFactory, artifactIdGenerator, fingerprintService, new());
    }
    public InventoryOrchestrationService(IStreamingDiscoveryService discovery, IInventoryRoutingService routing, IArtifactFactory artifactFactory,
        IArtifactIdGenerator artifactIdGenerator, IContentFingerprintService fingerprintService, IInventoryRetainedSnapshotStore snapshots,
        IInventoryParentJournal journal, LinuxInventorySnapshotWorkspace workspace, InventoryProcessingLimits? limits = null)
        : this(discovery, routing, artifactFactory, artifactIdGenerator, fingerprintService)
    { (_snapshots, _journal, _workspace) = (snapshots, journal, workspace); _limits = limits ?? new(); _limits.Validate(); }
    public IInventoryParentJournal Journal => _journal ?? throw new InvalidOperationException("Compose Inventory retained-input services.");
    public IInventoryRetainedSnapshotStore Snapshots => _snapshots ?? throw new InvalidOperationException("Compose Inventory retained-input services.");
    public Task<IAsyncDisposable> AcquireAsync(string parentId, CancellationToken ct) =>
        (_workspace ?? throw new InvalidOperationException("Compose Inventory owned workspace.")).AcquireAsync(parentId, ct);
    public async Task<InventoryParentState> AdmitOrLoadAsync(string workflowId, string operationId, string source, DiscoveryOptions options,
        InventoryMode mode, InventoryAuthorityBinding? authority, CancellationToken ct)
    {
        var existing = await Journal.LoadAsync(operationId, ct);
        if (existing is not null)
        {
            if (existing.Plan.WorkflowId != workflowId || existing.Plan.Mode != mode || existing.Plan.Authority != authority) throw new InvalidOperationException("Inventory operation binding changed.");
            return existing;
        }
        if (await Journal.HasRetainedPreparationAsync(operationId, ct)) throw new InvalidOperationException("Interrupted pre-admission Inventory preparation requires review; retained inputs are quarantined.");
        Statistics = new(); var items = new List<DiscoveredItem>();
        await foreach (var item in _discovery.DiscoverItemsAsync(source, options, ct))
        {
            Statistics.ItemsDiscovered++;
            if (_routing.SelectProvider(item) is null) { Statistics.ItemsSkipped++; continue; }
            if (item.SourcePath.Length > _limits.MaximumStringCharacters || items.Count >= _limits.MaximumPlanItems) throw new InvalidDataException("Inventory plan admission limit.");
            items.Add(item); Statistics.ItemsHandled++;
        }
        items.Sort((a, b) => string.CompareOrdinal(a.SourcePath, b.SourcePath));
        var planned = new List<InventoryPlanItem>(); long retained = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (planned.Count > 0 && planned[^1].SourceLocator == item.SourcePath) throw new InvalidDataException("Duplicate source locator.");
            var binding = await Snapshots.CaptureAsync(operationId, item.SourcePath, ct).ConfigureAwait(false);
            retained = checked(retained + binding.Length); if (retained > _limits.MaximumRetainedBytes) throw new InvalidDataException("Inventory retained aggregate budget exhausted.");
            var fingerprint = new EMF.Core.Models.Integrity.ContentFingerprint { Algorithm = "SHA-256", Value = binding.Fingerprint };
            var id = _ids.Generate(); var created = _factory.Create(item, id, fingerprint);
            if (created?.Artifact is null || created.Artifact.Id != id) throw new InvalidOperationException("Inventory factory returned an invalid artifact identity.");
            if (created.Artifact.Fingerprint != fingerprint) throw new InvalidOperationException("Inventory factory returned an invalid content fingerprint.");
            if (created.Provenance?.ArtifactId != id) throw new InvalidOperationException("Inventory factory returned an invalid provenance identity.");
            if (created.Provenance.Source != item.SourcePath) throw new InvalidOperationException("Inventory factory returned an invalid provenance source.");
            planned.Add(new(planned.Count, InventoryIdentity.New(), id.Value, item.SourcePath, binding, JsonSerializer.Serialize(new IngestionMetadataDraft(created.Artifact, created.Provenance))));
        }
        return await Journal.AdmitAsync(new(operationId, workflowId, operationId, mode, authority, planned.ToArray()), InventoryIdentity.New(), ct);
    }
    public async Task<InventoryOrchestrationResult> ReadAsync(InventoryPlanItem item, CancellationToken ct)
    {
        var draft = JsonSerializer.Deserialize<IngestionMetadataDraft>(item.DraftJson) ?? throw new InvalidDataException("Invalid admitted draft.");
        if (draft.Artifact.Id.Value != item.ArtifactId || draft.Provenance.ArtifactId != draft.Artifact.Id || draft.Provenance.Source != item.SourceLocator || draft.Artifact.Fingerprint?.Value != item.Retained.Fingerprint) throw new InvalidDataException("Inventory draft binding mismatch.");
        var discovered = new DiscoveredItem { Name = draft.Artifact.Name, SourcePath = item.SourceLocator, SourceType = draft.Artifact.ArtifactType, SizeBytes = item.Retained.Length };
        var provider = _routing.SelectProvider(discovered) ?? throw new InvalidDataException("Admitted Inventory provider unavailable.");
        await using var lease = await Snapshots.MaterializeAsync(item.Retained, ct).ConfigureAwait(false);
        var inventory = await provider.CreateInventoryAsync(lease.Path, ct).ConfigureAwait(false);
        var projected = new DatabaseInventory { DatabasePath = item.SourceLocator, DatabaseEngine = inventory.DatabaseEngine, DatabaseVersion = inventory.DatabaseVersion, InventoryDate = inventory.InventoryDate };
        projected.Tables.AddRange(inventory.Tables);
        return new() { DiscoveredItem = discovered, Artifact = draft.Artifact, Provenance = draft.Provenance, Success = true, Inventory = projected };
    }
    public async Task<byte[]> ReadContentAsync(InventoryPlanItem item, CancellationToken ct)
    {
        await using var lease = await Snapshots.MaterializeAsync(item.Retained, ct).ConfigureAwait(false);
        await using var stream = File.OpenRead(lease.Path);
        if (stream.Length != item.Retained.Length || stream.Length > _limits.MaximumPlaintextBytes) throw new InvalidDataException("Inventory plaintext byte limit.");
        var bytes = new byte[checked((int)stream.Length)]; try { await stream.ReadExactlyAsync(bytes, ct); if (stream.Position != stream.Length) throw new InvalidDataException(); return bytes; } catch { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); throw; }
    }
    public async IAsyncEnumerable<InventoryOrchestrationResult> ExecuteAsync(string sourcePath, DiscoveryOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);
        // This compatibility API returns inspections, not durable publication effects.
        // Only the workflow admits a durable parent and confirms persisted children.
        var ephemeral = _snapshots as IInventoryEphemeralSnapshotStore
            ?? throw new InvalidOperationException("Compose Inventory owned ephemeral snapshots.");
        Statistics = new();
        var watch = Stopwatch.StartNew();
        var items = new List<DiscoveredItem>();
        var owned = new List<(DiscoveredItem Item, InventorySnapshotLease Lease, ArtifactCreationResult Created)>();
        Exception? operationFailure = null;
        async Task<T> ObserveAsync<T>(Func<Task<T>> action)
        {
            try { return await action().ConfigureAwait(false); }
            catch (Exception failure) { operationFailure = failure; throw; }
        }
        try
        {
            await ObserveAsync(async () =>
            {
                await foreach (var item in _discovery.DiscoverItemsAsync(sourcePath, options, cancellationToken))
                {
                    Statistics.ItemsDiscovered++;
                    if (_routing.SelectProvider(item) is null) { Statistics.ItemsSkipped++; continue; }
                    if (item.SourcePath.Length > _limits.MaximumStringCharacters || items.Count >= _limits.MaximumPlanItems)
                        throw new InvalidDataException("Inventory plan admission limit.");
                    items.Add(item); Statistics.ItemsHandled++;
                }
                items.Sort((a, b) => string.CompareOrdinal(a.SourcePath, b.SourcePath));
                long retained = 0;
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (owned.Count > 0 && owned[^1].Item.SourcePath == item.SourcePath) throw new InvalidDataException("Duplicate source locator.");
                    var lease = await ephemeral.CaptureEphemeralAsync(item.SourcePath, cancellationToken).ConfigureAwait(false);
                    // Transfer ownership before any further validation can fail.
                    owned.Add((item, lease, null!));
                    var length = new FileInfo(lease.Path).Length;
                    retained = checked(retained + length);
                    if (retained > _limits.MaximumRetainedBytes) throw new InvalidDataException("Inventory retained aggregate budget exhausted.");
                    string hash;
                    await using (var stream = File.OpenRead(lease.Path))
                        hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                    var fingerprint = new EMF.Core.Models.Integrity.ContentFingerprint { Algorithm = "SHA-256", Value = hash };
                    var id = _ids.Generate();
                    var created = _factory.Create(item, id, fingerprint);
                    if (created?.Artifact is null || created.Artifact.Id != id) throw new InvalidOperationException("Inventory factory returned an invalid artifact identity.");
                    if (created.Artifact.Fingerprint != fingerprint) throw new InvalidOperationException("Inventory factory returned an invalid content fingerprint.");
                    if (created.Provenance?.ArtifactId != id) throw new InvalidOperationException("Inventory factory returned an invalid provenance identity.");
                    if (created.Provenance.Source != item.SourcePath) throw new InvalidOperationException("Inventory factory returned an invalid provenance source.");
                    owned[^1] = (item, lease, created);
                }
                return true;
            }).ConfigureAwait(false);
            foreach (var entry in owned)
            {
                var result = await ObserveAsync(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var provider = _routing.SelectProvider(entry.Item) ?? throw new InvalidDataException("Inventory provider unavailable.");
                    DatabaseInventory inventory;
                    try { inventory = await provider.CreateInventoryAsync(entry.Lease.Path, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch { Failed(); throw; }
                    var projected = new DatabaseInventory { DatabasePath = entry.Item.SourcePath, DatabaseEngine = inventory.DatabaseEngine, DatabaseVersion = inventory.DatabaseVersion, InventoryDate = inventory.InventoryDate };
                    projected.Tables.AddRange(inventory.Tables);
                    // Release this ephemeral input before publishing its completed inspection.
                    await entry.Lease.DisposeAsync().ConfigureAwait(false);
                    Statistics.InventoriesCompleted++;
                    return new InventoryOrchestrationResult() { DiscoveredItem = entry.Item, Artifact = entry.Created.Artifact, Provenance = entry.Created.Provenance, Success = true, Inventory = projected };
                }).ConfigureAwait(false);
                yield return result;
            }
        }
        finally
        {
            // Cancellation must not cancel ownership cleanup. Attempt every owned lease.
            List<Exception>? cleanupFailures = null;
            foreach (var entry in owned)
            {
                try { await entry.Lease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception failure) { (cleanupFailures ??= new()).Add(failure); }
            }
            Elapsed(watch.Elapsed);
            if (cleanupFailures is not null)
            {
                if (operationFailure is not null) cleanupFailures.Insert(0, operationFailure);
                throw new AggregateException("Inventory ephemeral cleanup failed.", cleanupFailures);
            }
        }
    }
    internal void Confirmed() { Statistics.InventoriesCompleted++; }
    internal void Failed() { Statistics.ItemsFailed++; }
    internal void Elapsed(TimeSpan elapsed) { Statistics.Elapsed = elapsed; }
}
