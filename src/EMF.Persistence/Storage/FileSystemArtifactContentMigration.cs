using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Storage;

/// <summary>Explicit single-host offline conversion. Never invoked by ordinary admission.</summary>
public sealed class FileSystemArtifactContentMigration
{
    private const string Gate = ".content-coordination";
    private const string Marker = ".content-format";
    private const string Catalog = ".content-catalog.sqlite";
    private const string Generations = ".content-generations";
    private readonly IContentStoragePlatform _platform;
    internal Func<string, Task>? Checkpoint { get; set; }
    public FileSystemArtifactContentMigration() : this(ContentStoragePlatform.Select()) { }
    internal FileSystemArtifactContentMigration(IContentStoragePlatform platform) => _platform = platform;
    private async Task At(string point) { if (Checkpoint is not null) await Checkpoint(point); }
    private sealed record Manifest(string Root, string MigrationId, string Retention, int ArtifactCount);
    private sealed record Entry(string Id, long Length, string Stamp, string Digest, string Generation, string Revision, string Status);

    internal static string WorkspaceFor(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return Path.Combine(Path.GetDirectoryName(root)!, ".emf-content-migration-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root))));
    }

    internal static void RequireCompletedWorkspace(string root, IContentStoragePlatform platform)
    {
        var workspace = WorkspaceFor(root);
        if (!Directory.Exists(workspace) && !File.Exists(workspace)) return;
        var manifest = ReadManifest(workspace, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), platform);
        var completed = Path.Combine(workspace, ".completed");
        if (!File.Exists(completed)) throw new ArtifactContentMigrationInProgressException();
        RejectLinks(completed); platform.ValidatePrivatePermissions(completed);
        if (File.ReadAllText(completed) != manifest.MigrationId)
            throw new InvalidDataException("Content migration completion identity is damaged.");
        // A completed authority permanently excludes fresh bootstrap, even if the
        // root itself was removed or replaced. This preflight is read-only.
        foreach (var name in new[] { ".content-coordination", ".content-format", ".content-catalog.sqlite" })
        {
            var path = Path.Combine(root, name); RejectLinks(path);
            if (!File.Exists(path)) throw new InvalidDataException("Completed migrated root is missing protocol state.");
            platform.ValidatePrivatePermissions(path);
        }
        foreach (var name in new[] { ".content-generations", manifest.Retention })
        {
            var path = Path.Combine(root, name); RejectLinks(path);
            if (!Directory.Exists(path)) throw new InvalidDataException("Completed migrated root is missing protocol state.");
            platform.ValidatePrivatePermissions(path);
        }
        if (File.ReadAllText(Path.Combine(root, ".content-format")) != "EMF-CONTENT-1")
            throw new InvalidDataException("Completed migrated root format is damaged.");
    }

    internal static void ValidateCompletedRun(string root, string migrationId, string retention, long artifactCount, IContentStoragePlatform platform)
    {
        RequireCompletedWorkspace(root, platform);
        var manifest = ReadManifest(WorkspaceFor(root), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), platform);
        if (manifest.MigrationId != migrationId || manifest.Retention != retention || manifest.ArtifactCount != artifactCount)
            throw new InvalidDataException("Catalog migration provenance does not match its durable descriptor.");
    }

    public async Task<ArtifactContentMigrationResult> MigrateAsync(string rootPath,
        ArtifactContentOfflineMigrationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.OldReadersAndWritersStopped)
            throw new InvalidOperationException("Offline migration requires explicit confirmation that all old readers and writers are stopped and remain stopped through cutover.");
        if (options.MaxStoredBytes <= 0 || options.MaxStoredBytes > Array.MaxLength || options.MaxArtifacts is < 1 or > 1000000 || options.MaxTotalBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Content root is required.", nameof(rootPath));
        cancellationToken.ThrowIfCancellationRequested(); _platform.RequirePlatform();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        RejectLinks(root);
        if (!Directory.Exists(root)) throw new InvalidDataException("Legacy root must already exist.");
        _platform.ValidatePrivatePermissions(root); _platform.RequireSameFileSystem(root, Path.GetDirectoryName(root)!); _platform.FlushDirectory(root, true);
        // This is the existing directory inode, not a reserved legacy filename.
        using var admission = await _platform.AcquireAdmissionAsync(root, cancellationToken);
        var workspace = WorkspaceFor(root);
        Manifest manifest;
        if (!Directory.Exists(workspace))
        {
            if (File.Exists(workspace)) throw new InvalidDataException("Migration workspace path is occupied.");
            var inventory = Inventory(root, options, cancellationToken);
            var migrationId = Guid.NewGuid().ToString("N");
            manifest = new(root, migrationId, ".content-legacy-" + migrationId, inventory.Count);
            if (Directory.GetFileSystemEntries(root).Any(path => Path.GetFileName(path) == manifest.Retention))
                throw new InvalidDataException("Migration retention namespace is occupied.");
            EstablishWorkspace(workspace, manifest, inventory);
            await At("InventoryPersisted");
        }
        else manifest = ReadManifest(workspace, root, _platform);
        var coordination = Path.Combine(workspace, ".migration-coordination");
        if (!File.Exists(coordination)) throw new InvalidDataException("Migration coordination identity is missing.");
        using var migration = await _platform.AcquireAsync(coordination, true, cancellationToken);
        var entries = ReadEntries(workspace, root, manifest, options);
        var phase = ReadPhase(workspace, root);
        if (phase == "Completed")
        {
            VerifyRetained(root, manifest, entries, options);
            await CompleteMarker(workspace, root, manifest);
            // Uses the normal lineage validator, including any genuine later mutations.
            await new FileSystemArtifactContentStore(root, options.MaxStoredBytes).ReadAuditObligationsAsync(null, 1, cancellationToken);
            return new(entries.Count, Path.Combine(root, manifest.Retention), true);
        }
        if (File.Exists(Path.Combine(workspace, ".completed"))) throw new InvalidDataException("Premature migration completion marker.");
        ValidateInventoryLocations(root, manifest, entries, phase);
        var stage = Path.Combine(workspace, "versioned");
        var gatePath = Path.Combine(stage, Gate);
        if (!File.Exists(gatePath))
        {
            var gateIntent = Path.Combine(workspace, "gate-publication");
            gatePath = Path.Combine(root, Gate);
            if (phase != "CuttingOver" || !File.Exists(gateIntent) || !File.Exists(gatePath))
                throw new InvalidDataException("Migration generation coordination identity is missing.");
            RejectLinks(gateIntent); _platform.ValidatePrivatePermissions(gateIntent);
            if (File.ReadAllText(gateIntent) != StableIdentity(_platform.InspectSourceFile(gatePath)))
                throw new InvalidDataException("Published migration generation gate identity conflicts.");
        }
        using var generationGate = await _platform.AcquireAsync(gatePath, true, cancellationToken);
        if (phase is "Inventory" or "Importing")
        {
            SetPhase(workspace, root, "Importing");
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(root, entry.Id);
                CheckSource(source, entry.Length, entry.Stamp, entry.Digest, _platform, options.MaxStoredBytes);
                if (entry.Status is "Pending" or "Copying")
                {
                    SetStatus(workspace, root, entry.Id, "Copying");
                    var destination = Path.Combine(stage, Generations, entry.Generation);
                    if (!File.Exists(destination))
                    {
                        var temp = Path.Combine(stage, Generations, Guid.NewGuid().ToString("N") + ".tmp");
                        await CopySource(source, temp, entry, options.MaxStoredBytes, cancellationToken);
                        await At("CandidateWritten");
                        File.Move(temp, destination);
                    }
                    CheckGeneration(destination, entry, options.MaxStoredBytes);
                    _platform.FlushDirectory(Path.Combine(stage, Generations));
                    await At("GenerationDurable");
                    using var connection = OpenCatalog(workspace, root);
                    using var transaction = connection.BeginTransaction(deferred: false);
                    using var command = connection.CreateCommand(); command.Transaction = transaction;
                    command.CommandText = "INSERT INTO ContentState(ArtifactId,Revision,Generation,Length,Owner) VALUES($id,$rev,$gen,$len,NULL); UPDATE ContentMigrationArtifacts SET Status='Imported' WHERE ArtifactId=$id AND Status='Copying'";
                    command.Parameters.AddWithValue("$id", entry.Id); command.Parameters.AddWithValue("$rev", entry.Revision);
                    command.Parameters.AddWithValue("$gen", entry.Generation); command.Parameters.AddWithValue("$len", entry.Length);
                    command.ExecuteNonQuery();
                    await At("BeforeArtifactCommit"); cancellationToken.ThrowIfCancellationRequested(); transaction.Commit();
                    await At("ArtifactCommitted");
                }
                VerifyExact(source, Path.Combine(stage, Generations, entry.Generation), entry, options.MaxStoredBytes);
                SetStatus(workspace, root, entry.Id, "Verified");
                await At("VerificationRecorded");
            }
            entries = ReadEntries(workspace, root, manifest, options);
            foreach (var entry in entries)
                VerifyExact(Path.Combine(root, entry.Id), Path.Combine(stage, Generations, entry.Generation), entry, options.MaxStoredBytes);
            SetPhase(workspace, root, "Ready"); await At("ReadyForCutover");
        }
        await Cutover(workspace, root, manifest, options, cancellationToken);
        await new FileSystemArtifactContentStore(root, options.MaxStoredBytes).ReadAuditObligationsAsync(null, 1, cancellationToken);
        return new(entries.Count, Path.Combine(root, manifest.Retention), false);
    }

    private List<Entry> Inventory(string root, ArtifactContentOfflineMigrationOptions options, CancellationToken ct)
    {
        var entries = Directory.EnumerateFileSystemEntries(root).Take(options.MaxArtifacts + 1).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).ToArray();
        if (entries.Length > options.MaxArtifacts) throw new InvalidDataException("Legacy inventory exceeds its artifact budget.");
        var result = new List<Entry>(); long total = 0;
        foreach (var path in entries)
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(path); ValidateLegacyName(id); RejectLinks(path);
            if (Directory.Exists(path)) throw new InvalidDataException("Legacy flat layout cannot contain directories.");
            var identity = _platform.InspectSourceFile(path);
            if (identity.Length < 0 || identity.Length > options.MaxStoredBytes || identity.Length > options.MaxTotalBytes - total)
                throw new InvalidDataException("Legacy content exceeds the migration byte budget.");
            total += identity.Length;
            // Recognizable versioned/preparing markers are ambiguous, never overwritten.
            if (id == Marker && identity.Length < 100)
            {
                using var markerSource = _platform.OpenSourceFile(path);
                using var markerReader = new StreamReader(markerSource);
                if (markerReader.ReadToEnd() is "EMF-CONTENT-1" or "EMF-CONTENT-PREPARING" or "EMF-CONTENT-MIGRATING")
                    throw new InvalidDataException("Recognizable protocol state requires reconciliation, not legacy import.");
            }
            var digest = HashFile(path, _platform, options.MaxStoredBytes);
            if (_platform.InspectSourceFile(path) != identity) throw new IOException("Legacy source changed during inventory.");
            result.Add(new(id, identity.Length, identity.Stamp, digest, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "Pending"));
        }
        return result;
    }

    internal static void ValidateLegacyName(string name)
    {
        _ = new ArtifactId(name);
        if (name is "." or ".." || name.Contains('/') || name.Contains('\\') || Path.IsPathRooted(name))
            throw new InvalidDataException("Legacy Artifact identity is not a pre-protocol flat filename.");
    }

    private void EstablishWorkspace(string workspace, Manifest manifest, List<Entry> inventory)
    {
        // Publish an entirely initialized checkpoint directory atomically. A death
        // before publication leaves only an unreferenced private sibling candidate.
        var temp = workspace + ".preparing-" + Guid.NewGuid().ToString("N");
        _platform.CreatePrivateDirectory(temp);
        WritePrivate(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(manifest));
        WritePrivate(Path.Combine(temp, ".migration-coordination"), "");
        var stage = Path.Combine(temp, "versioned"); _platform.CreatePrivateDirectory(stage);
        _platform.CreatePrivateDirectory(Path.Combine(stage, Generations));
        WritePrivate(Path.Combine(stage, Gate), ""); WritePrivate(Path.Combine(stage, Marker), "EMF-CONTENT-MIGRATING");
        WritePrivate(Path.Combine(stage, Catalog), "");
        using (var connection = OpenAt(stage, initializing: true))
        {
            using (var transaction = connection.BeginTransaction(deferred: false))
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = ContentCatalogSchema.Foundation; command.ExecuteNonQuery(); transaction.Commit();
            }
            ContentCatalogSchema.Upgrade(connection);
            using var tx = connection.BeginTransaction(deferred: false);
            using var run = connection.CreateCommand(); run.Transaction = tx;
            run.CommandText = "INSERT INTO ContentMigrationRun VALUES(1,$id,$root,$retention,'Inventory',$count,1)";
            run.Parameters.AddWithValue("$id", manifest.MigrationId); run.Parameters.AddWithValue("$root", manifest.Root);
            run.Parameters.AddWithValue("$retention", manifest.Retention); run.Parameters.AddWithValue("$count", inventory.Count); run.ExecuteNonQuery();
            foreach (var entry in inventory)
            {
                using var command = connection.CreateCommand(); command.Transaction = tx;
                command.CommandText = "INSERT INTO ContentMigrationArtifacts VALUES($id,$len,$stamp,$digest,$gen,$rev,'Pending')";
                command.Parameters.AddWithValue("$id", entry.Id); command.Parameters.AddWithValue("$len", entry.Length);
                command.Parameters.AddWithValue("$stamp", entry.Stamp); command.Parameters.AddWithValue("$digest", entry.Digest);
                command.Parameters.AddWithValue("$gen", entry.Generation); command.Parameters.AddWithValue("$rev", entry.Revision); command.ExecuteNonQuery();
            }
            tx.Commit();
        }
        _platform.FlushDirectory(Path.Combine(stage, Generations)); _platform.FlushDirectory(stage); _platform.FlushDirectory(temp);
        Directory.Move(temp, workspace); _platform.FlushDirectory(Path.GetDirectoryName(workspace)!);
    }

    private async Task Cutover(string workspace, string root, Manifest manifest, ArtifactContentOfflineMigrationOptions options, CancellationToken ct)
    {
        var entries = ReadEntries(workspace, root, manifest, options);
        var phase = ReadPhase(workspace, root);
        if (phase is not ("Ready" or "CuttingOver")) throw new InvalidDataException("Migration is not ready for cutover.");
        foreach (var entry in entries)
        {
            if (entry.Status is not ("Verified" or "Retained")) throw new InvalidDataException("Migration has unverified imports.");
            var source = SourceLocation(root, manifest, entry);
            VerifyExact(source, GenerationLocation(workspace, root, entry), entry, options.MaxStoredBytes);
        }
        SetPhase(workspace, root, "CuttingOver");
        var retention = Path.Combine(root, manifest.Retention);
        RejectLinks(retention);
        if (!Directory.Exists(retention)) { _platform.CreatePrivateDirectory(retention); _platform.FlushDirectory(root); }
        _platform.ValidatePrivatePermissions(retention);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var source = SourceLocation(root, manifest, entry);
            var retained = Path.Combine(retention, entry.Id);
            CheckSource(source, entry.Length, entry.Stamp, entry.Digest, _platform, options.MaxStoredBytes);
            if (source != retained)
            {
                File.Move(source, retained); _platform.FlushDirectory(retention); _platform.FlushDirectory(root);
                await At("LegacyRelocated");
            }
            CheckSource(retained, entry.Length, entry.Stamp, entry.Digest, _platform, options.MaxStoredBytes);
            SetStatus(workspace, root, entry.Id, "Retained");
        }
        VerifyRetained(root, manifest, ReadEntries(workspace, root, manifest, options), options);
        var stage = Path.Combine(workspace, "versioned");
        // Intent durably pins the actual staged catalog inode before its rename.
        // A missing checkpoint can never cause a legacy catalog-name collision to
        // be opened as SQL merely because it exists at the destination path.
        var intent = Path.Combine(workspace, "catalog-publication");
        await PublishControl(intent, StableIdentity(_platform.InspectSourceFile(CatalogLocation(workspace, root))));
        var gateIntent = Path.Combine(workspace, "gate-publication");
        await PublishControl(gateIntent, StableIdentity(_platform.InspectSourceFile(File.Exists(Path.Combine(stage, Gate)) ? Path.Combine(stage, Gate) : Path.Combine(root, Gate))));
        _platform.FlushDirectory(workspace);
        foreach (var path in Directory.GetFileSystemEntries(root))
            if (Path.GetFileName(path) != manifest.Retention && Path.GetFileName(path) is not (Gate or Generations or Catalog))
                throw new InvalidDataException("Unexpected source remains at cutover.");
        foreach (var name in new[] { Gate, Generations, Catalog })
        {
            var source = Path.Combine(stage, name); var destination = Path.Combine(root, name);
            if (File.Exists(source) || Directory.Exists(source))
            {
                if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidDataException("Cutover destination is occupied.");
                if (name == Generations) Directory.Move(source, destination); else File.Move(source, destination);
                _platform.FlushDirectory(root); _platform.FlushDirectory(stage);
                await At("ProtocolPublished");
            }
        }
        SetPhase(workspace, root, "Completed");
        await At("BeforeCompletionMarker");
        await CompleteMarker(workspace, root, manifest);
        await At("MigrationCompleted");
    }

    private async Task CompleteMarker(string workspace, string root, Manifest manifest)
    {
        var stage = Path.Combine(workspace, "versioned"); var staged = Path.Combine(stage, Marker); var destination = Path.Combine(root, Marker);
        if (File.Exists(staged))
        {
            RejectLinks(staged); _platform.ValidatePrivatePermissions(staged);
            using (var file = new FileStream(staged, FileMode.Truncate, FileAccess.Write, FileShare.None))
            { file.Write("EMF-CONTENT-1"u8); file.Flush(true); }
            if (File.Exists(destination)) throw new InvalidDataException("Cutover marker destination is occupied.");
            File.Move(staged, destination); _platform.FlushDirectory(root); _platform.FlushDirectory(stage);
        }
        if (!File.Exists(destination) || File.ReadAllText(destination) != "EMF-CONTENT-1")
            throw new InvalidDataException("Completed migration format marker is missing or damaged.");
        var completed = Path.Combine(workspace, ".completed");
        await PublishControl(completed, manifest.MigrationId);
        _platform.FlushDirectory(workspace);
    }

    private async Task PublishControl(string path, string value)
    {
        RejectLinks(path);
        if (File.Exists(path))
        {
            _platform.ValidatePrivatePermissions(path);
            if (File.ReadAllText(path) != value) throw new InvalidDataException("Migration control identity conflicts.");
            return;
        }
        var candidate = path + ".pending-" + Guid.NewGuid().ToString("N");
        WritePrivate(candidate, value);
        await At(Path.GetFileName(path) + "CandidateDurable");
        File.Move(candidate, path);
        _platform.FlushDirectory(Path.GetDirectoryName(path)!);
        await At(Path.GetFileName(path) + "Published");
    }

    private static string StableIdentity(ContentSourceIdentity identity) => string.Join(":", identity.Stamp.Split(':').Take(3));
    private string CatalogLocation(string workspace, string root)
    {
        var staged = Path.Combine(workspace, "versioned", Catalog);
        if (File.Exists(staged)) return staged;
        var intent = Path.Combine(workspace, "catalog-publication"); var published = Path.Combine(root, Catalog);
        if (!File.Exists(intent) || !File.Exists(published)) throw new InvalidDataException("Migration catalog checkpoint is missing.");
        RejectLinks(intent); _platform.ValidatePrivatePermissions(intent);
        if (File.ReadAllText(intent) != StableIdentity(_platform.InspectSourceFile(published)))
            throw new InvalidDataException("Migration catalog publication identity is damaged.");
        return published;
    }
    private SqliteConnection OpenCatalog(string workspace, string root) => OpenAt(Path.GetDirectoryName(CatalogLocation(workspace, root))!);
    private SqliteConnection OpenAt(string directory, bool initializing = false)
    {
        RejectLinks(directory); _platform.ValidatePrivatePermissions(directory);
        foreach (var name in new[] { Catalog, Catalog + "-journal", Catalog + "-wal", Catalog + "-shm" })
        {
            var path = Path.Combine(directory, name); RejectLinks(path);
            if (File.Exists(path)) _platform.ValidatePrivatePermissions(path);
        }
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, Catalog), Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            if (command.ExecuteScalar() as string != "delete") throw new InvalidDataException("Migration catalog requires DELETE journal mode.");
            command.CommandText = "PRAGMA synchronous=EXTRA; PRAGMA synchronous";
            if (Convert.ToInt32(command.ExecuteScalar()) != 3) throw new InvalidDataException("Migration catalog durability is unavailable.");
            if (!initializing) ContentCatalogSchema.Validate(connection);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private List<Entry> ReadEntries(string workspace, string root, Manifest manifest, ArtifactContentOfflineMigrationOptions options)
    {
        using var connection = OpenCatalog(workspace, root);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId,Root,Retention,ArtifactCount,OfflineAcknowledged,Phase FROM ContentMigrationRun WHERE Id=1";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != manifest.MigrationId || reader.GetString(1) != root || reader.GetString(2) != manifest.Retention ||
                reader.GetInt32(3) != manifest.ArtifactCount || reader.GetInt32(4) != 1 || reader.GetString(5) is not ("Inventory" or "Importing" or "Ready" or "CuttingOver" or "Completed") || reader.Read())
                throw new InvalidDataException("Migration identity/checkpoint is damaged.");
        }
        command.CommandText = "SELECT ArtifactId,Length,SourceStamp,Digest,Generation,Revision,Status FROM ContentMigrationArtifacts ORDER BY ArtifactId COLLATE BINARY";
        var result = new List<Entry>(); long total = 0;
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var entry = new Entry(rows.GetString(0), rows.GetInt64(1), rows.GetString(2), rows.GetString(3), rows.GetString(4), rows.GetString(5), rows.GetString(6));
            ValidateLegacyName(entry.Id);
            if (entry.Length < 0 || entry.Length > options.MaxStoredBytes || entry.Length > options.MaxTotalBytes - total || entry.Stamp.Length == 0 ||
                !IsHex(entry.Digest, 64) || !IsHex(entry.Generation, 32) || !IsHex(entry.Revision, 32) || entry.Status is not ("Pending" or "Copying" or "Imported" or "Verified" or "Retained"))
                throw new InvalidDataException("Migration artifact checkpoint is damaged.");
            total += entry.Length; result.Add(entry);
            if (result.Count > options.MaxArtifacts) throw new InvalidDataException("Migration inventory exceeds its budget.");
        }
        rows.Dispose();
        if (result.Count != manifest.ArtifactCount) throw new InvalidDataException("Migration inventory is incomplete.");
        var currentPhase = ReadPhase(workspace, root);
        foreach (var entry in result)
        {
            using var state = connection.CreateCommand();
            state.CommandText = "SELECT Revision,Generation,Length,Owner FROM ContentState WHERE ArtifactId=$id";
            state.Parameters.AddWithValue("$id", entry.Id);
            using var current = state.ExecuteReader();
            var committed = entry.Status is "Imported" or "Verified" or "Retained";
            if (current.Read())
            {
                // Once completion permits real mutations, ordinary lineage validation
                // is the authority. Before cutover it must match the checkpoint exactly.
                if (currentPhase != "Completed" && (!committed || current.GetString(0) != entry.Revision || current.GetString(1) != entry.Generation || current.GetInt64(2) != entry.Length || !current.IsDBNull(3)))
                    throw new InvalidDataException("Migration checkpoint and catalog state disagree.");
            }
            else if (committed) throw new InvalidDataException("Promoted migration state is missing.");
        }
        return result;
    }
    private string ReadPhase(string workspace, string root)
    {
        using var connection = OpenCatalog(workspace, root); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Phase FROM ContentMigrationRun WHERE Id=1";
        return command.ExecuteScalar() as string ?? throw new InvalidDataException("Migration phase is missing.");
    }
    private void SetPhase(string workspace, string root, string phase) => UpdateCheckpoint(workspace, root,
        "UPDATE ContentMigrationRun SET Phase=$value WHERE Id=1", phase, null);
    private void SetStatus(string workspace, string root, string id, string status) => UpdateCheckpoint(workspace, root,
        "UPDATE ContentMigrationArtifacts SET Status=$value WHERE ArtifactId=$id", status, id);
    private void UpdateCheckpoint(string workspace, string root, string sql, string value, string? id)
    {
        using var connection = OpenCatalog(workspace, root); using var tx = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = tx; command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value); if (id is not null) command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("Migration checkpoint transition failed.");
        tx.Commit();
    }
    private void ValidateInventoryLocations(string root, Manifest manifest, List<Entry> entries, string phase)
    {
        var allowed = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        if (phase == "CuttingOver") allowed.UnionWith(new[] { manifest.Retention, Gate, Catalog, Generations });
        foreach (var path in Directory.GetFileSystemEntries(root))
            if (!allowed.Contains(Path.GetFileName(path))) throw new InvalidDataException("Unexpected source entry appeared during migration.");
        foreach (var entry in entries) _ = SourceLocation(root, manifest, entry);
    }
    private static string SourceLocation(string root, Manifest manifest, Entry entry)
    {
        var original = Path.Combine(root, entry.Id); var retained = Path.Combine(root, manifest.Retention, entry.Id);
        var hasOriginal = File.Exists(original); var hasRetained = File.Exists(retained);
        // Protocol names at the original path after relocation are not source files.
        if (hasRetained)
        {
            if (hasOriginal && entry.Id is not (Gate or Catalog or Marker))
                throw new InvalidDataException("Both original and retained source paths are present.");
            RejectLinks(retained); return retained;
        }
        if (hasOriginal) { RejectLinks(original); return original; }
        throw new IOException("Inventoried legacy source is missing.");
    }
    private static string GenerationLocation(string workspace, string root, Entry entry)
    {
        var stagedDirectory = Path.Combine(workspace, "versioned", Generations);
        return Path.Combine(Directory.Exists(stagedDirectory) ? stagedDirectory : Path.Combine(root, Generations), entry.Generation);
    }
    private void VerifyRetained(string root, Manifest manifest, List<Entry> entries, ArtifactContentOfflineMigrationOptions options)
    {
        var retention = Path.Combine(root, manifest.Retention); RejectLinks(retention); _platform.ValidatePrivatePermissions(retention);
        if (!Directory.GetFileSystemEntries(retention).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal)
            .SequenceEqual(entries.Select(entry => entry.Id).OrderBy(name => name, StringComparer.Ordinal)))
            throw new InvalidDataException("Retained legacy inventory is contradictory.");
        foreach (var entry in entries)
        {
            if (entry.Status != "Retained") throw new InvalidDataException("Migration retention checkpoint is incomplete.");
            CheckSource(Path.Combine(retention, entry.Id), entry.Length, entry.Stamp, entry.Digest, _platform, options.MaxStoredBytes);
        }
    }

    private async Task CopySource(string source, string temp, Entry entry, long maxBytes, CancellationToken ct)
    {
        CheckSource(source, entry.Length, entry.Stamp, entry.Digest, _platform, maxBytes);
        using var input = _platform.OpenSourceFile(source);
        await using var output = _platform.CreatePrivateFile(temp, asynchronous: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long length = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                length += read; if (length > entry.Length) throw new IOException("Legacy source changed during copy.");
                hash.AppendData(buffer.AsSpan(0, read)); await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (length != entry.Length || Convert.ToHexString(hash.GetHashAndReset()) != entry.Digest || _platform.InspectSourceFile(source).Stamp != entry.Stamp)
                throw new IOException("Legacy source changed during copy.");
            await output.FlushAsync(ct); output.Flush(true);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private void CheckGeneration(string path, Entry entry, long maxBytes)
    {
        RejectLinks(path); _platform.ValidatePrivatePermissions(path);
        if (_platform.InspectSourceFile(path).Length != entry.Length || HashFile(path, _platform, maxBytes) != entry.Digest)
            throw new InvalidDataException("Imported generation does not reproduce its legacy source.");
    }
    private void VerifyExact(string source, string generation, Entry entry, long maxBytes)
    {
        CheckSource(source, entry.Length, entry.Stamp, entry.Digest, _platform, maxBytes); CheckGeneration(generation, entry, maxBytes);
        using var input = _platform.OpenSourceFile(source); using var output = _platform.OpenSourceFile(generation);
        var left = new byte[81920]; var right = new byte[81920]; long total = 0;
        try
        {
            int read;
            while ((read = input.Read(left)) != 0)
            {
                output.ReadExactly(right.AsSpan(0, read));
                if (!left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read))) throw new InvalidDataException("Migration readback differs from the retained legacy bytes.");
                total += read;
            }
            if (total != entry.Length || output.ReadByte() != -1) throw new IOException("Migration source size changed during verification.");
            CheckSource(source, entry.Length, entry.Stamp, entry.Digest, _platform, maxBytes);
        }
        finally { CryptographicOperations.ZeroMemory(left); CryptographicOperations.ZeroMemory(right); }
    }
    internal static void CheckSource(string path, long length, string stamp, string digest, IContentStoragePlatform platform, long maxBytes)
    {
        RejectLinks(path); var identity = platform.InspectSourceFile(path);
        if (identity.Length != length || identity.Stamp != stamp || HashFile(path, platform, maxBytes) != digest)
            throw new IOException("Inventoried legacy source identity or bytes changed.");
    }
    internal static string HashFile(string path, IContentStoragePlatform platform, long maxBytes)
    {
        RejectLinks(path); var before = platform.InspectSourceFile(path);
        if (before.Length < 0 || before.Length > maxBytes) throw new InvalidDataException("Migration source exceeds the byte budget.");
        using var stream = platform.OpenSourceFile(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long total = 0;
        try
        {
            int count; while ((count = stream.Read(buffer)) != 0)
            { total += count; if (total > maxBytes) throw new IOException("Migration source exceeds the byte budget."); hash.AppendData(buffer.AsSpan(0, count)); }
            if (total != before.Length || platform.InspectSourceFile(path) != before) throw new IOException("Migration source changed during integrity verification.");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private void WritePrivate(string path, string value)
    { using var file = _platform.CreatePrivateFile(path); file.Write(Encoding.UTF8.GetBytes(value)); file.Flush(true); }
    private static Manifest ReadManifest(string workspace, string root, IContentStoragePlatform platform)
    {
        RejectLinks(workspace); platform.ValidatePrivatePermissions(workspace);
        var path = Path.Combine(workspace, "manifest.json"); RejectLinks(path); platform.ValidatePrivatePermissions(path);
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Migration manifest exceeds its size bound.");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) ?? throw new InvalidDataException("Migration manifest is invalid.");
        if (manifest.Root != root || !IsHex(manifest.MigrationId, 32) || !manifest.Retention.StartsWith(".content-legacy-", StringComparison.Ordinal) ||
            !IsHex(manifest.Retention[16..], 32) || manifest.ArtifactCount is < 0 or > 1000000)
            throw new InvalidDataException("Migration manifest identity is invalid.");
        return manifest;
    }
    private static bool IsHex(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigit);
    internal static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Migration paths cannot contain symbolic links.");
        }
    }
}
