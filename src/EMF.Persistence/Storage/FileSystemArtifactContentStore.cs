using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Storage;

public sealed class FileSystemArtifactContentStore : IVersionedArtifactContentStore
{
    public const long DefaultMaxStoredBytes = 150L * 1024 * 1024;
    private const string Gate = ".content-coordination";
    private const string Marker = ".content-format";
    private const string Catalog = ".content-catalog.sqlite";
    private const string Generations = ".content-generations";
    private readonly IContentStoragePlatform _platform;
    private readonly string _rootPath;
    private readonly long _maxStoredBytes;
    // Narrow internal fault/coordination seam, unavailable to production callers.
    internal Func<string, Task>? Checkpoint { get; set; }

    public FileSystemArtifactContentStore(string rootPath, long maxStoredBytes = DefaultMaxStoredBytes)
        : this(rootPath, maxStoredBytes, ContentStoragePlatform.Select()) { }

    internal FileSystemArtifactContentStore(string rootPath, long maxStoredBytes, IContentStoragePlatform platform)
    {
        _platform = platform;
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (maxStoredBytes <= 0 || maxStoredBytes > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(maxStoredBytes));
        _rootPath = Path.GetFullPath(rootPath);
        _maxStoredBytes = maxStoredBytes;
    }
    private string PathFor(string name) => Path.Combine(_rootPath, name);
    private async Task At(string point) { if (Checkpoint is not null) await Checkpoint(point); }

    public async Task WriteAsync(ArtifactId artifactId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        => _ = await MutateAsync(artifactId, ArtifactContentMutationKind.LegacyWrite, null, content,
            new(ArtifactContentOperationId.New()), cancellationToken);
    public async Task DeleteAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
        => _ = await MutateAsync(artifactId, ArtifactContentMutationKind.LegacyDelete, null, default,
            new(ArtifactContentOperationId.New()), cancellationToken);
    public async Task<byte[]?> ReadAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
        => (await ReadVersionedAsync(artifactId, cancellationToken))?.Content;
    public Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => MutateAsync(id, ArtifactContentMutationKind.Create, null, content, context, cancellationToken);
    public Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => MutateAsync(id, ArtifactContentMutationKind.Replace, expected, content, context, cancellationToken);
    public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => MutateAsync(id, ArtifactContentMutationKind.Delete, expected, default, context, cancellationToken);

    public async Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        await AdmitAsync(cancellationToken);
        using var connection = OpenCatalog();
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = ReadState(connection, transaction, id);
        if (state?.Generation is null) return null;
        await At("ReaderSelected");
        var path = GenerationPath(state.Generation);
        RejectSymbolicLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != state.Length || stream.Length > _maxStoredBytes || stream.Length > Array.MaxLength)
            throw new InvalidDataException("Committed content generation size is invalid.");
        var bytes = new byte[(int)stream.Length];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            if (stream.Position != stream.Length) throw new IOException("Immutable content generation changed.");
            transaction.Commit();
            return new(bytes, new(state.Revision));
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }
    public async Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        ArtifactContentIdentity.Validate(operationId.Value);
        await AdmitAsync(cancellationToken);
        using var connection = OpenCatalog();
        return FindReceipt(connection, null, operationId)?.Receipt;
    }
    public async Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(
        ArtifactContentReceiptCursor? afterCursor, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterCursor is { Value: <= 0 }) throw new ArgumentException("Receipt cursor is invalid.", nameof(afterCursor));
        await AdmitAsync(cancellationToken);
        using var connection = OpenCatalog();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EnumerationSequence, Receipt FROM ContentReceipts WHERE AuditEventId IS NOT NULL AND EnumerationSequence > $after ORDER BY EnumerationSequence LIMIT $limit";
        command.Parameters.AddWithValue("$after", afterCursor?.Value ?? 0);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<ArtifactContentAuditObligation>();
        while (reader.Read()) { cancellationToken.ThrowIfCancellationRequested(); result.Add(new(new(reader.GetInt64(0)), DecodeReceipt(reader.GetString(1)))); }
        return result;
    }

    private async Task<ArtifactContentMutationResult> MutateAsync(ArtifactId id, ArtifactContentMutationKind kind,
        ArtifactContentRevision? expected, ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (content.Length > _maxStoredBytes) throw new InvalidDataException("Artifact content exceeds the maximum stored size.");
        // Freeze caller-owned memory so request identity and durable bytes agree.
        var stableContent = content.ToArray();
        try { return await MutateCoreAsync(id, kind, expected, stableContent, context, ct); }
        finally { CryptographicOperations.ZeroMemory(stableContent); }
    }
    private async Task<ArtifactContentMutationResult> MutateCoreAsync(ArtifactId id, ArtifactContentMutationKind kind,
        ArtifactContentRevision? expected, ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateId(id);
        ArgumentNullException.ThrowIfNull(context);
        ArtifactContentIdentity.Validate(context.OperationId.Value);
        if (expected is { } revision) ArtifactContentIdentity.Validate(revision.Value);
        if (kind is ArtifactContentMutationKind.Replace or ArtifactContentMutationKind.Delete && expected is null)
            throw new ArgumentException("Conditional mutation requires an expected revision.");
        if (context.OwnershipToken is { } owner) ArtifactContentIdentity.Validate(owner.Value);
        if (context.AuditEventId is { } audit) ArtifactContentIdentity.Validate(audit.Value);
        if (context.RequiresAuditObligation && context.AuditEventId is null)
            throw new ArgumentException("Required content mutation audit obligation identity is missing.");
        if (content.Length > _maxStoredBytes) throw new InvalidDataException("Artifact content exceeds the maximum stored size.");
        await AdmitAsync(ct);
        using var gate = await _platform.AcquireAsync(PathFor(Gate), false, ct);
        // Private canonical identity never becomes the public revision or log data.
        var request = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Artifact = id.Value, Kind = kind, Expected = expected?.Value,
            Owner = context.OwnershipToken?.Value, Audit = context.AuditEventId?.Value, RequiredAudit = context.RequiresAuditObligation,
            Payload = Convert.ToHexString(SHA256.HashData(content.Span))
        })));
        string? candidate = null;
        if (kind is not (ArtifactContentMutationKind.Delete or ArtifactContentMutationKind.LegacyDelete))
        {
            candidate = Guid.NewGuid().ToString("N");
            var temp = GenerationPath(candidate + ".tmp");
            await using (var stream = _platform.CreatePrivateFile(temp, asynchronous: true))
            {
                await stream.WriteAsync(content, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            await At("BeforeGenerationRename");
            File.Move(temp, GenerationPath(candidate));
            await At("GenerationPublished");
            _platform.FlushDirectory(PathFor(Generations));
            await At("CandidateDurable");
        }
        using var connection = OpenCatalog();
        ct.ThrowIfCancellationRequested();
        using var transaction = connection.BeginTransaction(deferred: false);
        var prior = FindReceipt(connection, transaction, context.OperationId);
        if (prior is not null)
        {
            if (prior.Request != request) throw new ArtifactContentIdempotencyException();
            transaction.Commit();
            return new(prior.Receipt);
        }
        var state = ReadState(connection, transaction, id);
        // Reject a damaged current generation even on destructive mutation.
        if (state?.Generation is { } current) ValidateGeneration(current, state.Length);
        var present = state?.Generation is not null;
        var outcome = kind switch
        {
            ArtifactContentMutationKind.Create when present => ArtifactContentMutationOutcome.AlreadyExists,
            ArtifactContentMutationKind.Replace or ArtifactContentMutationKind.Delete when !present => ArtifactContentMutationOutcome.Missing,
            ArtifactContentMutationKind.Replace or ArtifactContentMutationKind.Delete when expected?.Value != state!.Revision => ArtifactContentMutationOutcome.VersionConflict,
            ArtifactContentMutationKind.Delete when state!.Owner is not null && context.OwnershipToken?.Value != state.Owner => ArtifactContentMutationOutcome.VersionConflict,
            ArtifactContentMutationKind.LegacyDelete when !present => ArtifactContentMutationOutcome.Missing,
            _ when kind != ArtifactContentMutationKind.Create && context.OwnershipToken is { } constraint && state?.Owner != constraint.Value => ArtifactContentMutationOutcome.VersionConflict,
            ArtifactContentMutationKind.Delete or ArtifactContentMutationKind.LegacyDelete => ArtifactContentMutationOutcome.Deleted,
            _ when present => ArtifactContentMutationOutcome.Replaced,
            _ => ArtifactContentMutationOutcome.Created
        };
        var changes = outcome is ArtifactContentMutationOutcome.Created or ArtifactContentMutationOutcome.Replaced or ArtifactContentMutationOutcome.Deleted;
        var next = changes ? Guid.NewGuid().ToString("N") : state?.Revision;
        var nextOwner = outcome == ArtifactContentMutationOutcome.Created ? context.OwnershipToken?.Value : state?.Owner;
        if (changes)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO ContentState(ArtifactId,Revision,Generation,Length,Owner) VALUES($id,$rev,$gen,$length,$owner) ON CONFLICT(ArtifactId) DO UPDATE SET Revision=$rev,Generation=$gen,Length=$length,Owner=$owner";
            command.Parameters.AddWithValue("$id", id.Value); command.Parameters.AddWithValue("$rev", next!);
            command.Parameters.AddWithValue("$gen", outcome == ArtifactContentMutationOutcome.Deleted ? DBNull.Value : candidate!);
            command.Parameters.AddWithValue("$length", outcome == ArtifactContentMutationOutcome.Deleted ? 0 : content.Length);
            command.Parameters.AddWithValue("$owner", (object?)nextOwner ?? DBNull.Value); command.ExecuteNonQuery();
        }
        var receipt = new ArtifactContentMutationReceipt(context.OperationId, id, kind,
            state is null ? null : new(state.Revision), next is null ? null : new(next),
            nextOwner is null ? null : new(nextOwner), outcome, DateTimeOffset.UtcNow, context.AuditEventId);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO ContentReceipts(OperationId,Request,Receipt,AuditEventId,PublishedGeneration,MutationRevision) VALUES($op,$request,$receipt,$audit,$generation,$mutationRevision)";
            command.Parameters.AddWithValue("$op", context.OperationId.Value); command.Parameters.AddWithValue("$request", request);
            command.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, ReceiptJson));
            command.Parameters.AddWithValue("$audit", (object?)context.AuditEventId?.Value ?? DBNull.Value);
            command.Parameters.AddWithValue("$generation", changes && outcome != ArtifactContentMutationOutcome.Deleted ? candidate! : DBNull.Value);
            command.Parameters.AddWithValue("$mutationRevision", changes ? next! : DBNull.Value);
            command.ExecuteNonQuery();
        }
        await At("BeforeCommit");
        ct.ThrowIfCancellationRequested();
        try { transaction.Commit(); }
        catch
        {
            // A failed acknowledgement is not proof of rollback. Reconcile using
            // a new connection, never issue another operation identity.
            transaction.Dispose();
            using var recovery = OpenCatalog();
            var known = FindReceipt(recovery, null, context.OperationId);
            if (known is not null && known.Request == request) return new(known.Receipt);
            throw;
        }
        await At("Committed");
        return new(receipt);
    }

    private async Task AdmitAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _platform.RequirePlatform();
        RejectSymbolicLinks(_rootPath);
        // Read-only preflight must precede any gate creation or permission mutation.
        if (Directory.Exists(_rootPath)) InspectUnmarkedRoot();
        await At("AdmissionPreflight");
        var parent = Path.GetDirectoryName(_rootPath)!;
        if (!Directory.Exists(parent)) throw new InvalidDataException("Content root parent must exist before admission.");
        if (!Directory.Exists(_rootPath))
        {
            _platform.CreatePrivateDirectory(_rootPath);
            _platform.FlushDirectory(parent, true);
        }
        // Recognized roots use only the normal producer gate/catalog protocol.
        // Directory coordination is solely for an undecided bootstrap layout.
        if (TryAdmitCurrentRoot()) return;
        await At("BeforeAdmissionCoordination");
        // The existing directory inode is read-only bootstrap coordination. Unlike
        // the producer gate, acquiring it inserts no entry into a legacy root.
        using var admission = await _platform.AcquireAdmissionAsync(_rootPath, ct);
        InspectUnmarkedRoot();
        _platform.ValidatePrivatePermissions(_rootPath);
        _platform.FlushDirectory(_rootPath, true);
        foreach (var name in new[] { Gate, Marker, Catalog, Catalog + "-journal", Generations }) RejectSymbolicLinks(PathFor(name));
        ValidateProtocolPermissions();
        if (TryAdmitCurrentRoot()) return;
        using var gate = await _platform.AcquireAsync(PathFor(Gate), true, ct);
        InspectUnmarkedRoot(gateCreatedByCurrentBootstrap: true); // This attempt alone may admit its newly created gate.
        ValidateProtocolPermissions();
        _platform.FlushDirectory(_rootPath);
        var initialized = File.Exists(PathFor(Marker));
        if (!initialized)
        {
            if (File.Exists(PathFor(Catalog)) || Directory.Exists(PathFor(Generations)))
                throw new InvalidDataException("Incomplete versioned content root requires reconciliation.");
            if (Directory.EnumerateFileSystemEntries(_rootPath).Any(p => Path.GetFileName(p) != Gate))
                throw new ArtifactContentMigrationRequiredException();
            // Marker precedes catalog creation: interrupted bootstrap is damage,
            // never a reason to erase or silently reinterpret this root.
            using (var marker = _platform.CreatePrivateFile(PathFor(Marker)))
            { marker.Write("EMF-CONTENT-PREPARING"u8); marker.Flush(true); }
            _platform.FlushDirectory(_rootPath);
            _platform.CreatePrivateDirectory(PathFor(Generations));
            using (var file = _platform.CreatePrivateFile(PathFor(Catalog))) file.Flush(true);
            using var connection = OpenCatalog(initializing: true);
            using var transaction = connection.BeginTransaction(deferred: false);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE ContentState(ArtifactId TEXT PRIMARY KEY,Revision TEXT NOT NULL,Generation TEXT,Length INTEGER NOT NULL,Owner TEXT);
                CREATE TABLE ContentReceipts(EnumerationSequence INTEGER PRIMARY KEY AUTOINCREMENT,OperationId TEXT NOT NULL UNIQUE,Request TEXT NOT NULL,Receipt TEXT NOT NULL,AuditEventId TEXT,PublishedGeneration TEXT,MutationRevision TEXT UNIQUE);
                PRAGMA user_version=2;
                """;
            command.ExecuteNonQuery(); transaction.Commit();
            _platform.FlushDirectory(_rootPath);
            using (var ready = new FileStream(PathFor(Marker), FileMode.Truncate, FileAccess.Write, FileShare.None))
            { ready.Write("EMF-CONTENT-1"u8); ready.Flush(true); }
            _platform.FlushDirectory(_rootPath);
        }
        if (!File.Exists(PathFor(Catalog)) || !Directory.Exists(PathFor(Generations)) || File.ReadAllText(PathFor(Marker)) != "EMF-CONTENT-1")
            throw new InvalidDataException("Versioned content root is damaged; reconciliation is required.");
        using var validated = OpenCatalog();
    }
    private bool TryAdmitCurrentRoot()
    {
        if (!File.Exists(PathFor(Marker))) return false;
        RejectSymbolicLinks(PathFor(Marker));
        _platform.ValidatePrivatePermissions(_rootPath);
        _platform.FlushDirectory(_rootPath, true);
        ValidateProtocolPermissions();
        if (!File.Exists(PathFor(Gate)))
            throw new InvalidDataException("Versioned content coordination identity is missing; reconciliation is required.");
        if (File.ReadAllText(PathFor(Marker)) != "EMF-CONTENT-1") return false;
        if (!File.Exists(PathFor(Catalog)) || !Directory.Exists(PathFor(Generations)))
            throw new InvalidDataException("Versioned content root is damaged; reconciliation is required.");
        using var checkedCatalog = OpenCatalog();
        return true;
    }

    private void InspectUnmarkedRoot(bool gateCreatedByCurrentBootstrap = false)
    {
        if (File.Exists(PathFor(Marker))) return;
        var names = Directory.EnumerateFileSystemEntries(_rootPath).Select(Path.GetFileName).ToArray();
        if (names.Any(name => name is Marker or Catalog or Generations || name!.StartsWith(Catalog + "-", StringComparison.Ordinal)))
            throw new InvalidDataException("Incomplete versioned content root requires reconciliation.");
        // A pre-protocol Artifact could be named exactly like the producer gate.
        // Only the gate created by this attempt after the locked empty-root check
        // can be accepted without a format marker. Interrupted gate-only roots
        // remain ambiguous and require explicit reconciliation/migration.
        if (names.Any(name => !gateCreatedByCurrentBootstrap || name != Gate))
            throw new ArtifactContentMigrationRequiredException();
    }

    private void ValidateProtocolPermissions()
    {
        foreach (var name in new[] { Gate, Marker, Catalog, Catalog + "-journal", Catalog + "-wal", Catalog + "-shm", Generations })
        {
            var path = PathFor(name);
            RejectSymbolicLinks(path);
            if (File.Exists(path) || Directory.Exists(path))
            {
                try { _platform.ValidatePrivatePermissions(path); }
                // SQLite removes DELETE journals after committing. Their absence is valid.
                catch (FileNotFoundException) when (name == Catalog + "-journal") { }
            }
        }
        if (Directory.Exists(PathFor(Generations)))
            foreach (var path in Directory.EnumerateFileSystemEntries(PathFor(Generations)))
            {
                RejectSymbolicLinks(path);
                try { _platform.ValidatePrivatePermissions(path); }
                // A concurrent writer may publish an uncommitted temporary generation.
                // Catalog-referenced generations are separately required and validated.
                catch (FileNotFoundException) when (path.EndsWith(".tmp", StringComparison.Ordinal)) { }
            }
    }

    private SqliteConnection OpenCatalog(bool initializing = false)
    {
        // SQLite can touch rollback/WAL/SHM paths while opening or querying
        // journal mode. Reject unsafe paths before handing the catalog to SQLite.
        _platform.ValidatePrivatePermissions(_rootPath);
        ValidateProtocolPermissions();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = PathFor(Catalog), Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            if (!string.Equals((string?)command.ExecuteScalar(), "delete", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Content catalog requires DELETE journal mode.");
            command.CommandText = "PRAGMA synchronous=EXTRA; PRAGMA synchronous";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 3)
                throw new InvalidDataException("Content catalog requires EXTRA durability.");
            if (!initializing)
            {
                command.CommandText = "PRAGMA user_version";
                if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 2)
                    throw new InvalidDataException("Content catalog schema version is unsupported.");
                command.CommandText = "SELECT ArtifactId, Revision, Generation, Length, Owner FROM ContentState LIMIT 0; SELECT OperationId, Request, Receipt, AuditEventId, PublishedGeneration, MutationRevision, EnumerationSequence FROM ContentReceipts LIMIT 0";
                command.ExecuteNonQuery();
                ValidateCatalogState(connection);
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    private void ValidateCatalogState(SqliteConnection connection)
    {
        // Milestone 1 favors complete admission validation over an unchecked
        // fast path. Future optimization must preserve these fail-closed checks.
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA quick_check";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Content catalog integrity check failed.");
        var receipts = new Dictionary<(string Artifact, string Revision), (ArtifactContentMutationReceipt Receipt, string? Generation)>();
        command.CommandText = "SELECT OperationId, Request, Receipt, AuditEventId, PublishedGeneration, MutationRevision, EnumerationSequence FROM ContentReceipts";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var receipt = DecodeReceipt(reader.GetString(2));
                var audit = reader.IsDBNull(3) ? null : reader.GetString(3);
                var request = reader.GetString(1);
                var changes = receipt.Outcome is ArtifactContentMutationOutcome.Created or ArtifactContentMutationOutcome.Replaced or ArtifactContentMutationOutcome.Deleted;
                var mutationRevision = reader.IsDBNull(5) ? null : reader.GetString(5);
                if (reader.GetInt64(6) <= 0 || receipt.OperationId.Value != reader.GetString(0) || receipt.AuditEventId?.Value != audit ||
                    request.Length != 64 || request.Any(c => !char.IsAsciiHexDigit(c)) ||
                    mutationRevision != (changes ? receipt.CurrentRevision?.Value : null))
                    throw new InvalidDataException("Content receipt identity is contradictory.");
                if (receipt.Outcome is ArtifactContentMutationOutcome.Created or ArtifactContentMutationOutcome.Replaced or ArtifactContentMutationOutcome.Deleted)
                {
                    if (receipt.CurrentRevision is null || receipt.CurrentRevision == receipt.PriorRevision ||
                        !receipts.TryAdd((receipt.ArtifactId.Value, receipt.CurrentRevision.Value.Value), (receipt, reader.IsDBNull(4) ? null : reader.GetString(4))))
                        throw new InvalidDataException("Content receipt revision is contradictory.");
                }
            }
        }
        foreach (var ((artifact, _), published) in receipts)
            if (published.Receipt.PriorRevision is { } previous && !receipts.ContainsKey((artifact, previous.Value)))
                throw new InvalidDataException("Content mutation lineage is incomplete.");
        var stateArtifacts = new HashSet<string>(StringComparer.Ordinal);
        command.CommandText = "SELECT ArtifactId, Revision, Generation, Length, Owner FROM ContentState";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var artifact = new ArtifactId(reader.GetString(0)).Value;
                stateArtifacts.Add(artifact);
                var revision = ArtifactContentIdentity.Validate(reader.GetString(1));
                var generation = reader.IsDBNull(2) ? null : reader.GetString(2);
                var length = reader.GetInt64(3);
                var owner = reader.IsDBNull(4) ? null : ArtifactContentIdentity.Validate(reader.GetString(4));
                if (!receipts.TryGetValue((artifact, revision), out var published) || published.Receipt.OwnershipToken?.Value != owner ||
                    published.Generation != generation ||
                    (generation is null) != (published.Receipt.Outcome == ArtifactContentMutationOutcome.Deleted) || length < 0 ||
                    generation is null && length != 0)
                    throw new InvalidDataException("Content catalog state is contradictory.");
                if (generation is not null) ValidateGeneration(generation, length);
            }
        }
        if (receipts.Keys.Any(key => !stateArtifacts.Contains(key.Artifact)))
            throw new InvalidDataException("Content mutation lineage has no current/tombstone state.");
        transaction.Commit();
    }
    private sealed record State(string Revision, string? Generation, long Length, string? Owner);
    private static State? ReadState(SqliteConnection connection, SqliteTransaction transaction, ArtifactId id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Revision,Generation,Length,Owner FROM ContentState WHERE ArtifactId=$id";
        command.Parameters.AddWithValue("$id", id.Value);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    }
    private sealed record StoredReceipt(string Request, ArtifactContentMutationReceipt Receipt);
    private static StoredReceipt? FindReceipt(SqliteConnection connection, SqliteTransaction? transaction, ArtifactContentOperationId id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Request,Receipt FROM ContentReceipts WHERE OperationId=$id";
        command.Parameters.AddWithValue("$id", id.Value);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), DecodeReceipt(reader.GetString(1))) : null;
    }
    private static ArtifactContentMutationReceipt DecodeReceipt(string json)
    {
        var receipt = JsonSerializer.Deserialize<ArtifactContentMutationReceipt>(json, ReceiptJson)
            ?? throw new InvalidDataException("Invalid content mutation receipt.");
        ArtifactContentIdentity.Validate(receipt.OperationId.Value);
        _ = new ArtifactId(receipt.ArtifactId.Value);
        if (!Enum.IsDefined(receipt.Kind) || !Enum.IsDefined(receipt.Outcome) ||
            receipt.OccurredUtc.Offset != TimeSpan.Zero || receipt.AuditObligationVersion != 1)
            throw new InvalidDataException("Content receipt fields are invalid.");
        return receipt;
    }
    private static readonly JsonSerializerOptions ReceiptJson = new()
    { Converters = { new ArtifactIdConverter() } };
    private sealed class ArtifactIdConverter : JsonConverter<ArtifactId>
    {
        public override ArtifactId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, ArtifactId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
    private string GenerationPath(string value)
    {
        if (!(value.Length == 32 || value.Length == 36 && value.EndsWith(".tmp", StringComparison.Ordinal)) ||
            value[..32].Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid content generation identity.");
        return Path.Combine(PathFor(Generations), value);
    }
    private void ValidateGeneration(string value, long length)
    {
        var path = GenerationPath(value); RejectSymbolicLinks(path);
        if (File.Exists(path)) _platform.ValidatePrivatePermissions(path);
        if (!File.Exists(path) || new FileInfo(path).Length != length || length > _maxStoredBytes)
            throw new InvalidDataException("Committed content generation is damaged.");
    }
    private static void ValidateId(ArtifactId id)
    {
        // ArtifactId is logical catalog data. Its existing Core contract governs
        // syntax; neither protocol-identity limits nor filesystem rules apply.
        _ = new ArtifactId(id.Value);
    }
    private static void RejectSymbolicLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Artifact content paths cannot contain symbolic links.");
        }
    }
}
