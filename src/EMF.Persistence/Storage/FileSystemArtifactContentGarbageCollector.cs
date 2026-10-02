namespace EMF.Persistence.Storage;

/// <summary>Explicit physical reclamation; never changes logical history.</summary>
public sealed class FileSystemArtifactContentGarbageCollector : IDisposable
{
    /// <summary>Hard inspection ceiling per invocation; total sweep size has no policy ceiling.</summary>
    public const int MaximumNamespaceEntriesPerCall = 100000;
    private readonly FileSystemArtifactContentStore _store;
    private readonly SemaphoreSlim _invocation = new(1, 1);
    private bool _disposed;
    internal Func<string, Task>? Checkpoint { get; set; }

    public FileSystemArtifactContentGarbageCollector(string rootPath,
        long maxStoredBytes = FileSystemArtifactContentStore.DefaultMaxStoredBytes)
        => _store = new(rootPath, maxStoredBytes);

    internal FileSystemArtifactContentGarbageCollector(string rootPath, IContentStoragePlatform platform)
        => _store = new(rootPath, FileSystemArtifactContentStore.DefaultMaxStoredBytes, platform);

    public async Task<ArtifactContentGarbageCollectionResult> CollectAsync(
        ArtifactContentGarbageCollectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!await _invocation.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
            throw new TimeoutException("Concurrent garbage collection invocation timed out.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await _store.CollectGenerationsAsync(options ?? new(), Checkpoint, cancellationToken);
        }
        catch { _store.ResetGenerationSweep(); throw; }
        finally { _invocation.Release(); }
    }
    /// <summary>Release an abandoned inspection continuation. Do not call from a checkpoint callback.</summary>
    public void Dispose()
    {
        _invocation.Wait();
        try { _disposed = true; _store.ResetGenerationSweep(); }
        finally { _invocation.Release(); }
    }
}

/// <param name="MaxNamespaceEntries">Maximum namespace entries inspected in this call; never a total namespace ceiling.</param>
/// <param name="MaxEntriesSelected">Maximum entries considered for reclamation in this batch.</param>
/// <param name="MaxFilesReclaimed">Maximum physical unlinks in this batch.</param>
/// <param name="Continuation">Opaque live-collector sweep token. Resume using this field.</param>
public sealed record ArtifactContentGarbageCollectionOptions(
    int MaxNamespaceEntries = FileSystemArtifactContentGarbageCollector.MaximumNamespaceEntriesPerCall,
    int MaxEntriesSelected = 1000,
    int MaxFilesReclaimed = 100,
    string? Continuation = null);

public sealed record ArtifactContentGarbageCollectionResult(
    int NamespaceEntries, int EntriesSelected, int FilesReclaimed, long BytesReclaimed,
    bool SweepComplete,
    string? Continuation = null, int EntriesInspected = 0, bool ValidationComplete = true, bool ValidationRestarted = false);

public sealed partial class FileSystemArtifactContentStore
{
    private sealed class GenerationSweep : IDisposable
    {
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public IGenerationNamespaceWatch Watch { get; }
        public IEnumerator<string> Entries { get; }
        public PriorityQueue<(string Path, ContentSourceIdentity Identity), string> Inventory { get; } = new(StringComparer.Ordinal);
        public int Count { get; set; }
        public bool Validated { get; set; }
        public GenerationSweep(string directory, IContentStoragePlatform platform)
        {
            Watch = platform.CreateGenerationNamespaceWatch(directory);
            try { Entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator(); }
            catch { Watch.Dispose(); throw; }
        }
        public void Dispose() { Entries.Dispose(); Watch.Dispose(); }
    }
    private GenerationSweep? _generationSweep;
    internal void ResetGenerationSweep()
    {
        _generationSweep?.Dispose();
        _generationSweep = null;
    }

    private static bool IsProviderGenerationName(string name)
        => (name.Length == 32 || name.Length == 36 && name.EndsWith(".tmp", StringComparison.Ordinal)) &&
           name[..32].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal async Task<ArtifactContentGarbageCollectionResult> CollectGenerationsAsync(
        ArtifactContentGarbageCollectionOptions options, Func<string, Task>? checkpoint, CancellationToken ct)
    {
        if (options.MaxNamespaceEntries is < 1 or > FileSystemArtifactContentGarbageCollector.MaximumNamespaceEntriesPerCall ||
            options.MaxEntriesSelected is < 1 or > 10000 || options.MaxFilesReclaimed is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.Continuation is not null && options.Continuation != _generationSweep?.Token)
            throw new ArgumentException("Unknown or expired collector continuation; restart without continuation.", nameof(options));
        // Normal stores still validate all generation permissions. GC admits only
        // protocol/catalog state here and validates namespace entries incrementally.
        IncrementalGenerationInspection = true;
        if (options.Continuation is null) ResetGenerationSweep();
        await AdmitAsync(ct);
        if (checkpoint is not null) await checkpoint("BeforeExclusiveGate");
        using var gate = await _platform.AcquireAsync(PathFor(Gate), true, ct);
        if (checkpoint is not null) await checkpoint("ExclusiveGateAcquired");
        ValidateAdmittedStructure();
        using var connection = OpenCatalog(); // bounded existing busy timeout (2 seconds)
        // BEGIN IMMEDIATE alone is insufficient: rollback-journal readers must clear
        // before any unlink, and new readers must remain blocked until durability.
        using var begin = connection.CreateCommand();
        // Raw SQL is intentional: the managed non-deferred transaction API issues
        // BEGIN IMMEDIATE. Commands below share this connection-owned transaction.
        begin.CommandText = "BEGIN EXCLUSIVE";
        begin.ExecuteNonQuery();
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateAdmittedStructure();
            FileSystemArtifactContentMigration.RequireCompletedWorkspace(_rootPath, _platform);
            ContentCatalogSchema.Validate(connection);
            var origins = ValidateCatalogState(connection, null);
            // GC is infrequent and destructive: reverify retained byte evidence even
            // on a previously admitted instance, under the exclusive coordination.
            MigrationOrigins.VerifyEvidence(origins, _rootPath, _platform, _maxStoredBytes);
            var protectedNames = origins.Values.Select(origin => origin.Generation).ToHashSet(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Generation FROM ContentState WHERE Generation IS NOT NULL";
                using var reader = command.ExecuteReader();
                while (reader.Read()) protectedNames.Add(reader.GetString(0));
            }
            var restarted = false;
            if (_generationSweep is not null && _generationSweep.Watch.Changed())
            {
                ResetGenerationSweep();
                restarted = true;
            }
            var newSweep = _generationSweep is null;
            var sweep = _generationSweep ??= new GenerationSweep(PathFor(Generations), _platform);
            if (newSweep && checkpoint is not null) await checkpoint("NamespaceWatchEstablished");
            var inspected = 0;
            if (!sweep.Validated)
            {
                // Preserve the enumerator rather than rescanning/skipping a prefix.
                // No lookahead entry is inspected beyond this call's budget.
                while (inspected < options.MaxNamespaceEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!sweep.Entries.MoveNext()) { sweep.Validated = true; sweep.Entries.Dispose(); break; }
                    var path = sweep.Entries.Current;
                    var name = Path.GetFileName(path);
                    if (!IsProviderGenerationName(name)) throw new InvalidDataException("Unknown generation namespace entry requires review.");
                    RejectSymbolicLinks(path);
                    _platform.ValidatePrivatePermissions(path);
                    sweep.Inventory.Enqueue((path, _platform.InspectSourceFile(path)), name);
                    sweep.Count++;
                    inspected++;
                    if (checkpoint is not null) await checkpoint("NamespaceEntryValidated");
                }
                sweep.Watch.RequireUnchanged();
            }
            if (!sweep.Validated)
            {
                using var validationCommit = connection.CreateCommand();
                validationCommit.CommandText = "COMMIT";
                validationCommit.ExecuteNonQuery();
                return new(sweep.Count, 0, 0, 0, false,
                    sweep.Token, inspected, false, restarted);
            }
            if (checkpoint is not null) await checkpoint("EligibilityRechecked");
            var selected = 0;
            var reclaimed = 0;
            long bytes = 0;
            sweep.Watch.RequireUnchanged();
            using var protection = connection.CreateCommand();
            protection.CommandText = "SELECT EXISTS(SELECT 1 FROM ContentState WHERE Generation=$generation) OR EXISTS(SELECT 1 FROM ContentMigrationArtifacts WHERE Generation=$generation)";
            var generationParameter = protection.Parameters.Add("$generation", Microsoft.Data.Sqlite.SqliteType.Text);
            protection.Prepare();
            while (sweep.Inventory.Count > 0)
            {
                if (selected == options.MaxEntriesSelected || reclaimed == options.MaxFilesReclaimed) break;
                ct.ThrowIfCancellationRequested();
                var (path, identity) = sweep.Inventory.Dequeue();
                var name = Path.GetFileName(path);
                selected++;
                if (protectedNames.Contains(name)) continue;
                if (checkpoint is not null) await checkpoint("BeforeUnlink");
                sweep.Watch.RequireUnchanged();
                RejectSymbolicLinks(path);
                // Missing eligible entries are an idempotent physical retry. Any
                // other identity/metadata change fails closed, never guesses.
                if (!File.Exists(path))
                {
                    if (Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
                        throw new IOException("Generation entry changed type.");
                    continue;
                }
                _platform.ValidatePrivatePermissions(path);
                if (_platform.InspectSourceFile(path) != identity)
                    throw new IOException("Generation identity changed during reclamation.");
                // The cached protected set is only a fast exclusion. Every unlink
                // queries authoritative current state and migration provenance anew
                // on this connection, still inside BEGIN EXCLUSIVE and the gate.
                generationParameter.Value = name;
                if (Convert.ToInt64(protection.ExecuteScalar()) != 0) continue;
                if (checkpoint is not null) await checkpoint("UnlinkProtectionRechecked");
                sweep.Watch.RequireUnchanged();
                File.Delete(path);
                sweep.Watch.AcknowledgeOwnDeletion(name);
                reclaimed++;
                bytes += identity.Length;
                if (checkpoint is not null) await checkpoint("Unlinked");
            }
            // Also flush on a no-op retry: a previous process may have died after
            // unlink, before acknowledgement of the directory durability barrier.
            if (checkpoint is not null) await checkpoint("BeforeDirectoryFlush");
            sweep.Watch.RequireUnchanged();
            _platform.FlushDirectory(PathFor(Generations));
            if (checkpoint is not null) await checkpoint("DirectoryFlushed");
            using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT";
            commit.ExecuteNonQuery();
            var complete = sweep.Inventory.Count == 0;
            var result = new ArtifactContentGarbageCollectionResult(sweep.Count, selected, reclaimed, bytes,
                complete, complete ? null : sweep.Token, inspected, true, restarted);
            if (complete) ResetGenerationSweep();
            return result;
        }
        catch
        {
            ResetGenerationSweep();
            using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            try { rollback.ExecuteNonQuery(); }
            catch { /* Connection disposal releases coordination; preserve the original failure. */ }
            throw;
        }
    }
}
