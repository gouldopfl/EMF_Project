using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Inventory.Models;
using EMF.Inventory.Persistence;
using EMF.Inventory.Providers;
using EMF.Inventory.Storage;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Storage;
using EMF.Security.Ingestion;
using Microsoft.Data.Sqlite;

namespace EMF.Tests.TestInfrastructure;

internal sealed class InventoryMigrationFixture : IAsyncDisposable
{
    public ArtifactIngestionFixture Ingestion { get; private set; } = null!;
    public string Root => Ingestion.Root;
    public string Source => Path.Combine(Root, "sources");
    public string SourceFile => Path.Combine(Source, "a.sqlite");
    public InventoryProcessingLimits Limits { get; private set; } = new();
    public SqliteInventoryParentJournal Journal { get; private set; } = null!;
    public LinuxInventorySnapshotWorkspace Workspace { get; private set; } = null!;
    public FileSystemArtifactContentStore Physical { get; private set; } = null!;
    public InventoryProtectedSnapshotStorageAdapter Protection { get; private set; } = null!;
    public SqliteInventoryRetainedSnapshotStore Snapshots { get; private set; } = null!;
    public InventoryOrchestrationService Service { get; private set; } = null!;
    public InventoryAuthorityBinding Authority { get; set; } = new("host-authority", "revision-1", "synthetic-ingestion-actor");
    public WorkflowExecutionContext Context { get; private set; } = new() { WorkflowId = new("inventory-workflow"), OperationId = new(Guid.NewGuid().ToString("N")) };
    public static async Task<InventoryMigrationFixture> CreateAsync(InventoryProcessingLimits? limits = null)
    {
        var f = new InventoryMigrationFixture { Ingestion = await ArtifactIngestionFixture.CreateAsync(), Limits = limits ?? new() };
        Directory.CreateDirectory(f.Source); await f.SqlAsync(f.SourceFile, "CREATE TABLE evidence(id INTEGER PRIMARY KEY, name TEXT);INSERT INTO evidence(name) VALUES('one');");
        f.ComposeInventory(); return f;
    }
    // Fault injection is installed only in the dying runtime; restart never carries it forward.
    public Func<IArtifactIngestionPersistence, IArtifactIngestionPersistence>? ChildPersistenceOverride { get; set; }
    public async Task RestartAsync(Func<string, CancellationToken, Task>? backupCheckpoint = null)
    {
        var previous = Ingestion;
        var previousJournal = Journal; var previousWorkspace = Workspace;
        var previousPhysical = Physical; var previousProtection = Protection;
        var previousSnapshots = Snapshots; var previousService = Service;
        // All provider objects are connection/session factories, not live sessions. Checkpoints
        // unwind those sessions before restart. Clear only these databases' pooled connections;
        // DisposeAsync would delete the backing files and is reserved for final fixture cleanup.
        foreach (var path in new[] { previous.DatabasePath, previous.AuditPath })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(connection);
        }
        Context = new() { WorkflowId = Context.WorkflowId, OperationId = Context.OperationId };
        Ingestion = await ArtifactIngestionFixture.OpenWorkerAsync(previous.Root, previous.Id,
            new(ArtifactContentOperationId.New(), new(Guid.NewGuid().ToString("N")), Authority.ActorId)).ConfigureAwait(false);
        // OpenWorkerAsync reconstitutes the immutable synthetic host configuration: Confidential
        // classification, ingestion/recovery permission, and the development key identity/material.
        // Mutable requests, hooks, review lists, encryption counters and fault wrappers are not copied.
        ChildPersistenceOverride = null;
        ComposeInventory(backupCheckpoint);
        foreach (var pair in new (object Old, object New)[]
        {
            (previous, Ingestion), (previous.Repository, Ingestion.Repository),
            (previous.Persistence, Ingestion.Persistence), (previous.Physical, Ingestion.Physical),
            (previous.Staging, Ingestion.Staging), (previous.Audit, Ingestion.Audit),
            (previous.SecurityContext, Ingestion.SecurityContext), (previous.Classification, Ingestion.Classification),
            (previous.Authorization, Ingestion.Authorization), (previous.Encryption, Ingestion.Encryption),
            (previous.Fingerprints, Ingestion.Fingerprints), (previousJournal, Journal),
            (previousWorkspace, Workspace), (previousPhysical, Physical), (previousProtection, Protection),
            (previousSnapshots, Snapshots), (previousService, Service)
        }) Xunit.Assert.NotSame(pair.Old, pair.New);
        Xunit.Assert.Equal(0, Ingestion.Encryption.Encryptions);
        Xunit.Assert.Empty(Ingestion.Authorization.Requests);
        Xunit.Assert.Empty(Ingestion.SecurityContext.Reviews);
    }
    private void ComposeInventory(Func<string, CancellationToken, Task>? backupCheckpoint = null)
    {
        Workspace = new(Path.Combine(Root, "inventory-workspace"));
        Journal = new(Path.Combine(Root, "inventory-journal", "parent.sqlite"), Limits);
        Physical = new(Path.Combine(Root, "inventory-retained"), Limits.MaximumProtectedBytes);
        Protection = new(Physical, Ingestion.Encryption, Limits);
        Snapshots = new(Journal, Protection, Workspace, Limits, backupCheckpoint);
        Service = new(new InventoryBoundedDiscoveryService(Limits), new InventoryRoutingService(new[] { new SqliteInventoryProvider(Limits) }),
            new ArtifactFactory(), new GuidArtifactIdGenerator(), Ingestion.Fingerprints, Snapshots, Journal, Workspace, Limits);
    }
    public Task<InventoryParentState> PlanAsync(InventoryMode mode = InventoryMode.MetadataOnly) => Service.AdmitOrLoadAsync(Context.WorkflowId.Value, Context.OperationId!.Value.Value, Source, new(), mode, mode == InventoryMode.ProtectedContent ? Authority : null, default);
    public InventoryWorkflowActivity Activity(InventoryMode mode = InventoryMode.MetadataOnly, Func<string, CancellationToken, Task>? checkpoint = null)
        => new(Service, new EvidencePersistenceService(Ingestion.Repository), Source, new(), mode, mode == InventoryMode.ProtectedContent ? Child() : null, checkpoint);
    public IInventoryChildIngestionAdapter Child() => new InventoryChildIngestionAdapter((expected, ct) =>
    {
        if (expected is not null && expected != Authority) throw new UnauthorizedAccessException("Stale authority revision.");
        return Task.FromResult(Authority);
    }, (parent, item, ct) =>
    {
        Ingestion.SecurityContext.Operation = new(new(item.ChildOperationId), new(item.ChildOperationId), parent.Authority!.ActorId, new(parent.ParentId));
        // This is authoritative synthetic host composition; adoption revalidation is performed
        // by the existing authorization policy at the irreversible boundary as well.
        Ingestion.Authorization.Hook = request => parent.Authority == Authority ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException("Stale authority."));
        return Task.FromResult(new InventoryAuthenticatedChild(ChildPersistenceOverride?.Invoke(Ingestion.Persistence) ?? Ingestion.Persistence, Ingestion.Physical, Ingestion.Staging, Ingestion.Encryption, Ingestion.SecurityContext, Ingestion.Classification, Ingestion.Authorization, Ingestion.Audit, Ingestion.Fingerprints));
    });
    public async Task SqlAsync(string path, string sql)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync();
    }
    public async Task<InventoryParentState> ConfirmMetadataAsync(InventoryParentState state)
    {
        var item = state.Plan.Items[state.ConfirmedOrdinal + 1]; await Journal.StartNextAsync(state, item.Ordinal);
        return await Journal.ConfirmNextAsync(state, new(item.Ordinal, item.ChildOperationId, InventoryConfirmationDisposition.MetadataPersisted, item.ArtifactId, "durable-metadata-test-evidence"));
    }
    public ValueTask DisposeAsync() => Ingestion.DisposeAsync();
}
