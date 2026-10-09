using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Storage;

public sealed partial class FileSystemArtifactContentStore : IPreparedArtifactContentStore, IBoundedVersionedArtifactContentStore
{
    public const long DefaultMaxStoredBytes = 150L * 1024 * 1024;
    private const string Gate = ".content-coordination";
    private const string Marker = ".content-format";
    private const string Catalog = ".content-catalog.sqlite";
    private const string Generations = ".content-generations";
    private readonly IContentStoragePlatform _platform;
    private readonly string _rootPath;
    private readonly long _maxStoredBytes;
    private readonly ArtifactContentInspectionLimits _inspectionLimits;
    internal Action<string>? InspectionCheckpoint { get; set; }
    private ContentInspectionBudget Inspection(CancellationToken ct) => new(_inspectionLimits, ct, InspectionCheckpoint);
    internal bool IncrementalGenerationInspection { get; set; }
    private readonly SemaphoreSlim _admission = new(1, 1);
    private volatile bool _admitted;
    private Dictionary<(string Artifact, string Revision), MigrationOrigin>? _verifiedOrigins;
    // Narrow internal fault/coordination seam, unavailable to production callers.
    internal Func<string, Task>? Checkpoint { get; set; }
    internal Action<byte[]>? BoundedReadAllocated { get; set; }

    public FileSystemArtifactContentStore(string rootPath, long maxStoredBytes = DefaultMaxStoredBytes,
        ArtifactContentInspectionLimits? inspectionLimits = null)
        : this(rootPath, maxStoredBytes, ContentStoragePlatform.Select(), inspectionLimits) { }

    internal FileSystemArtifactContentStore(string rootPath, long maxStoredBytes, IContentStoragePlatform platform,
        ArtifactContentInspectionLimits? inspectionLimits = null)
    {
        _platform = platform;
        _inspectionLimits = inspectionLimits ?? new(); _inspectionLimits.Validate();
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (maxStoredBytes <= 0 || maxStoredBytes > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(maxStoredBytes));
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
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
        using var inspection = Inspection(cancellationToken);
        ValidateId(id);
        await AdmitAsync(cancellationToken, inspection);
        using var connection = OpenCatalog(inspection: inspection);
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
    public long MaximumStoredRepresentationBytes => _maxStoredBytes;
    public async Task<IArtifactContentReadLease?> ReadBoundedVersionedAsync(ArtifactId id,
        BoundedArtifactContentReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var inspection = Inspection(cancellationToken);
        ValidateId(id);
        await AdmitAsync(cancellationToken, inspection);
        using var connection = OpenCatalog(inspection: inspection);
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = ReadState(connection, transaction, id);
        if (state?.Generation is null) return null;
        var revision = new ArtifactContentRevision(state.Revision);
        if (request.ExpectedRevision is { } expected && revision != expected)
            throw new InvalidOperationException("Selected current content revision does not match.");
        if (state.Length < 0) throw new InvalidDataException("Committed content length is invalid.");
        await At("ReaderSelected");
        var path = GenerationPath(state.Generation);
        RejectSymbolicLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        if (length != state.Length || length > _maxStoredBytes || length > Array.MaxLength ||
            length > request.MaximumStoredRepresentationBytes || length > request.MaximumReturnedContentBytes)
            throw new InvalidDataException("Committed content generation exceeds bounded read limits or has invalid size.");
        await At("BeforeReadAllocation");
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = new byte[checked((int)length)];
        try
        {
            BoundedReadAllocated?.Invoke(bytes);
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            if (stream.Position != length || stream.Length != length) throw new IOException("Immutable content generation changed.");
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return new ArtifactContentReadLease(id, revision, length, bytes);
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }
    public async Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        using var inspection = Inspection(cancellationToken);
        ArtifactContentIdentity.Validate(operationId.Value);
        await AdmitAsync(cancellationToken, inspection);
        using var connection = OpenCatalog(inspection: inspection);
        return FindReceipt(connection, null, operationId)?.Receipt;
    }
    public async Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(
        ArtifactContentReceiptCursor? afterCursor, int limit, CancellationToken cancellationToken = default)
    {
        using var inspection = Inspection(cancellationToken);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterCursor is { Value: <= 0 }) throw new ArgumentException("Receipt cursor is invalid.", nameof(afterCursor));
        await AdmitAsync(cancellationToken, inspection);
        using var connection = OpenCatalog(inspection: inspection);
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
        try
        {
            await using var prepared = await PrepareMutationAsync(id, kind, expected, stableContent, context, ct);
            try { return await prepared.ExecuteAsync(ct); }
            catch (ContentCommitAcknowledgementException)
            {
                using var inspection = Inspection(ct);
                using var recovery = OpenCatalog(inspection: inspection);
                var known = FindReceipt(recovery, null, context.OperationId);
                if (known is not null && known.Request == ((PreparedPhysicalMutation)prepared).Request) return new(known.Receipt);
                throw;
            }
        }
        finally { CryptographicOperations.ZeroMemory(stableContent); }
    }
    public async Task<IPreparedArtifactContentMutation> PreparePhysicalCreateAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
    {
        if (content.Length > _maxStoredBytes) throw new InvalidDataException("Artifact content exceeds the maximum stored size.");
        var stable = content.ToArray();
        try { return await PrepareMutationAsync(id, ArtifactContentMutationKind.Create, null, stable, context, cancellationToken); }
        finally { CryptographicOperations.ZeroMemory(stable); }
    }
    public Task<IPreparedArtifactContentMutation> PreparePhysicalDeleteAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => PrepareMutationAsync(id, ArtifactContentMutationKind.Delete, expected, default, context, cancellationToken);
    public async Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(ArtifactId id, CancellationToken cancellationToken = default)
    {
        await using var probe = await PrepareRevisionValidationAsync(id, cancellationToken);
        return await probe.ReadCurrentRevisionAsync(cancellationToken);
    }
    public async Task<IArtifactContentRevisionProbe> PrepareRevisionValidationAsync(ArtifactId id, CancellationToken cancellationToken = default)
    {
        using var inspection = Inspection(cancellationToken);
        ValidateId(id); await AdmitAsync(cancellationToken, inspection);
        var connection = OpenCatalog(inspection: inspection); // Full integrity scan is detached; its read transaction ends here.
        try { return new RevisionProbe(this, id, connection, ReadState(connection, null, id)); }
        catch { connection.Dispose(); throw; }
    }
    private sealed class RevisionProbe(FileSystemArtifactContentStore store, ArtifactId id, SqliteConnection connection, State? admitted) : IArtifactContentRevisionProbe
    {
        private bool _disposed;
        public Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
            store.ValidateAdmittedStructure();
            var current = ReadState(connection, null, id);
            // A legitimate mutation changes revision. Same-revision state substitution is damage.
            if (current?.Revision == admitted?.Revision && current != admitted)
                throw new InvalidDataException("Current generation changed without a physical revision.");
            if (current?.Generation is null) return Task.FromResult<ArtifactContentRevision?>(null);
            store.ValidateGeneration(current.Generation, current.Length);
            return Task.FromResult<ArtifactContentRevision?>(new(current.Revision));
        }
        public ValueTask DisposeAsync() { if (!_disposed) { _disposed = true; connection.Dispose(); } return ValueTask.CompletedTask; }
    }
    private async Task<IPreparedArtifactContentMutation> PrepareMutationAsync(ArtifactId id, ArtifactContentMutationKind kind,
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
        using (var admissionInspection = Inspection(ct))
            await AdmitAsync(ct, admissionInspection);
        var gate = await _platform.AcquireAsync(PathFor(Gate), false, ct);
        try
        {
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
            using var inspection = Inspection(ct); // Fresh bounded revalidation after physical I/O, before the authority fence.
            var connection = OpenCatalog(inspection: inspection); // Complete catalog/namespace validation; no inspection CTS reaches payload I/O.
            try
            {
                await At("CatalogIntegrityPrepared");
                inspection.Check();
                return new PreparedPhysicalMutation(this, id, kind, expected, context, request, candidate, content.Length, gate, connection, ReadState(connection, null, id));
            }
            catch { connection.Dispose(); throw; }
        }
        catch { gate.Dispose(); throw; }
    }
    private sealed class PreparedPhysicalMutation(FileSystemArtifactContentStore store, ArtifactId id,
        ArtifactContentMutationKind kind, ArtifactContentRevision? expected, ArtifactContentMutationContext context,
        string request, string? candidate, int length, IDisposable gate, SqliteConnection connection, State? admitted) : IPreparedArtifactContentMutation
    {
        internal string Request => request;
        private int _executed;
        private bool _disposed;
        public Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Interlocked.Exchange(ref _executed, 1) != 0) throw new InvalidOperationException("Prepared mutation is single-use; reconcile by OperationId.");
            return store.PromotePreparedAsync(connection, id, kind, expected, context, request, candidate, length, admitted, cancellationToken);
        }
        public ValueTask DisposeAsync() { if (!_disposed) { _disposed = true; connection.Dispose(); gate.Dispose(); } return ValueTask.CompletedTask; }
    }
    private async Task<ArtifactContentMutationResult> PromotePreparedAsync(SqliteConnection connection, ArtifactId id, ArtifactContentMutationKind kind,
        ArtifactContentRevision? expected, ArtifactContentMutationContext context, string request, string? candidate, int length, State? admitted, CancellationToken ct)
    {
        ValidateAdmittedStructure();
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
        if (state?.Revision == admitted?.Revision && state != admitted)
            throw new InvalidDataException("Current generation changed without a physical revision.");
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
            command.Parameters.AddWithValue("$length", outcome == ArtifactContentMutationOutcome.Deleted ? 0 : length);
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
        catch (Exception error)
        {
            // Never infer rollback. Prepared callers release their authority session
            // before detached receipt reconciliation; no full integrity scan runs here.
            transaction.Dispose();
            throw new ContentCommitAcknowledgementException(error);
        }
        await At("Committed");
        return new(receipt);
    }

    private sealed class ContentCommitAcknowledgementException(Exception error)
        : IOException("Physical commit acknowledgement is unknown; reconcile the original OperationId.", error);

    private async Task AdmitAsync(CancellationToken ct, ContentInspectionBudget? inspection = null)
    {
        using var ownedInspection = inspection is null ? Inspection(ct) : null;
        inspection ??= ownedInspection!;
        inspection.Check();
        ct.ThrowIfCancellationRequested();
        FileSystemArtifactContentMigration.RequireCompletedWorkspace(_rootPath, _platform);
        if (_admitted) { ValidateAdmittedStructure(); return; }
        await _admission.WaitAsync(ct);
        try
        {
            if (_admitted) { ValidateAdmittedStructure(); return; }
            await AdmitCoreAsync(ct, inspection);
            inspection.Check();
            _admitted = true;
        }
        catch (SqliteException) when (inspection.IsStopped)
        { inspection.ThrowIfStopped(); throw; }
        finally { _admission.Release(); }
    }

    private string ReadFormatMarker()
    {
        // A format marker has a fixed small vocabulary; never allocate arbitrary
        // attacker-controlled file contents while inspecting an admitted root.
        if (new FileInfo(PathFor(Marker)).Length > 64)
            throw new InvalidDataException("Content format marker exceeds its size bound.");
        return File.ReadAllText(PathFor(Marker));
    }

    private void ValidateAdmittedStructure()
    {
        RejectSymbolicLinks(_rootPath);
        _platform.ValidatePrivatePermissions(_rootPath);
        foreach (var name in new[] { Gate, Marker, Catalog })
        {
            RejectSymbolicLinks(PathFor(name));
            if (!File.Exists(PathFor(name))) throw new InvalidDataException("Admitted content protocol state is missing.");
            _platform.ValidatePrivatePermissions(PathFor(name));
        }
        RejectSymbolicLinks(PathFor(Generations));
        if (!Directory.Exists(PathFor(Generations)) || ReadFormatMarker() != "EMF-CONTENT-1")
            throw new InvalidDataException("Admitted content protocol state is damaged.");
        _platform.ValidatePrivatePermissions(PathFor(Generations));
    }

    private async Task AdmitCoreAsync(CancellationToken ct, ContentInspectionBudget inspection)
    {
        ct.ThrowIfCancellationRequested();
        _platform.RequirePlatform();
        RejectSymbolicLinks(_rootPath);
        FileSystemArtifactContentMigration.RequireCompletedWorkspace(_rootPath, _platform);
        // Read-only preflight must precede any gate creation or permission mutation.
        if (Directory.Exists(_rootPath)) InspectUnmarkedRoot(inspection: inspection);
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
        if (await TryAdmitCurrentRootAsync(ct, inspection)) return;
        await At("BeforeAdmissionCoordination");
        // The existing directory inode is read-only bootstrap coordination. Unlike
        // the producer gate, acquiring it inserts no entry into a legacy root.
        using var admission = await _platform.AcquireAdmissionAsync(_rootPath, ct);
        FileSystemArtifactContentMigration.RequireCompletedWorkspace(_rootPath, _platform);
        InspectUnmarkedRoot(inspection: inspection);
        _platform.ValidatePrivatePermissions(_rootPath);
        _platform.FlushDirectory(_rootPath, true);
        foreach (var name in new[] { Gate, Marker, Catalog, Catalog + "-journal", Generations }) RejectSymbolicLinks(PathFor(name));
        ValidateProtocolPermissions(inspection);
        if (await TryAdmitCurrentRootAsync(ct, inspection)) return;
        using var gate = await _platform.AcquireAsync(PathFor(Gate), true, ct);
        InspectUnmarkedRoot(gateCreatedByCurrentBootstrap: true, inspection: inspection); // This attempt alone may admit its newly created gate.
        ValidateProtocolPermissions(inspection);
        _platform.FlushDirectory(_rootPath);
        var initialized = File.Exists(PathFor(Marker));
        if (!initialized)
        {
            if (File.Exists(PathFor(Catalog)) || Directory.Exists(PathFor(Generations)))
                throw new InvalidDataException("Incomplete versioned content root requires reconciliation.");
            if (Directory.EnumerateFileSystemEntries(_rootPath).Any(p => { inspection.Entry(p); return Path.GetFileName(p) != Gate; }))
                throw new ArtifactContentMigrationRequiredException();
            // Marker precedes catalog creation: interrupted bootstrap is damage,
            // never a reason to erase or silently reinterpret this root.
            using (var marker = _platform.CreatePrivateFile(PathFor(Marker)))
            { marker.Write("EMF-CONTENT-PREPARING"u8); marker.Flush(true); }
            _platform.FlushDirectory(_rootPath);
            _platform.CreatePrivateDirectory(PathFor(Generations));
            using (var file = _platform.CreatePrivateFile(PathFor(Catalog))) file.Flush(true);
            using var connection = OpenCatalog(initializing: true, inspection: inspection);
            using var sqlInspection = inspection.InspectSql(connection);
            using var transaction = connection.BeginTransaction(deferred: false);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = ContentCatalogSchema.Foundation;
            command.ExecuteNonQuery(); transaction.Commit();
            ContentCatalogSchema.Upgrade(connection);
            _platform.FlushDirectory(_rootPath);
            using (var ready = new FileStream(PathFor(Marker), FileMode.Truncate, FileAccess.Write, FileShare.None))
            { ready.Write("EMF-CONTENT-1"u8); ready.Flush(true); }
            _platform.FlushDirectory(_rootPath);
        }
        if (!File.Exists(PathFor(Catalog)) || !Directory.Exists(PathFor(Generations)) || ReadFormatMarker() != "EMF-CONTENT-1")
            throw new InvalidDataException("Versioned content root is damaged; reconciliation is required.");
        using var validated = OpenCatalog(fullMigrationEvidence: true, inspection: inspection);
    }
    private async Task<bool> TryAdmitCurrentRootAsync(CancellationToken ct, ContentInspectionBudget inspection)
    {
        if (!File.Exists(PathFor(Marker))) return false;
        RejectSymbolicLinks(PathFor(Marker));
        _platform.ValidatePrivatePermissions(_rootPath);
        _platform.FlushDirectory(_rootPath, true);
        ValidateProtocolPermissions(inspection);
        if (!File.Exists(PathFor(Gate)))
            throw new InvalidDataException("Versioned content coordination identity is missing; reconciliation is required.");
        if (ReadFormatMarker() != "EMF-CONTENT-1") return false;
        if (!File.Exists(PathFor(Catalog)) || !Directory.Exists(PathFor(Generations)))
            throw new InvalidDataException("Versioned content root is damaged; reconciliation is required.");
        // Recognized version 2 roots upgrade only under the stable exclusive
        // generation gate. A current root requires no additional gate acquisition.
        using (var catalogInspection = OpenCatalog(initializing: true, inspection: inspection))
        {
            int currentVersion;
            using (inspection.InspectSql(catalogInspection))
            using (var version = catalogInspection.CreateCommand())
            {
                version.CommandText = "PRAGMA user_version";
                currentVersion = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            if (currentVersion == 2)
            {
                using var gate = await _platform.AcquireAsync(PathFor(Gate), true, ct);
                using var sqlInspection = inspection.InspectSql(catalogInspection);
                ContentCatalogSchema.Upgrade(catalogInspection, (connection, transaction) => ValidateCatalogState(connection, transaction, foundationOnly: true, inspection: inspection));
                _platform.FlushDirectory(_rootPath);
            }
        }
        using var checkedCatalog = OpenCatalog(fullMigrationEvidence: true, inspection: inspection);
        return true;
    }

    private void InspectUnmarkedRoot(bool gateCreatedByCurrentBootstrap = false, ContentInspectionBudget? inspection = null)
    {
        using var work = inspection?.Inspect();
        if (File.Exists(PathFor(Marker))) return;
        var names = Directory.EnumerateFileSystemEntries(_rootPath).Select(path => { inspection?.Entry(path); return Path.GetFileName(path); }).ToArray();
        if (names.Any(name => name is Marker or Catalog or Generations || name!.StartsWith(Catalog + "-", StringComparison.Ordinal)))
            throw new InvalidDataException("Incomplete versioned content root requires reconciliation.");
        // A pre-protocol Artifact could be named exactly like the producer gate.
        // Only the gate created by this attempt after the locked empty-root check
        // can be accepted without a format marker. Interrupted gate-only roots
        // remain ambiguous and require explicit reconciliation/migration.
        if (names.Any(name => !gateCreatedByCurrentBootstrap || name != Gate))
            throw new ArtifactContentMigrationRequiredException();
    }

    private void ValidateProtocolPermissions(ContentInspectionBudget? inspection = null)
    {
        using var work = inspection?.Inspect();
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
        // The collector's private store validates this namespace through bounded
        // watch-backed inspection. Ordinary store admission keeps its full checks.
        if (Directory.Exists(PathFor(Generations)) && !IncrementalGenerationInspection)
            foreach (var path in Directory.EnumerateFileSystemEntries(PathFor(Generations)))
            {
                inspection?.Entry(path);
                RejectSymbolicLinks(path);
                try { _platform.ValidatePrivatePermissions(path); }
                // A concurrent writer may publish an uncommitted temporary generation.
                // Catalog-referenced generations are separately required and validated.
                catch (FileNotFoundException) when (path.EndsWith(".tmp", StringComparison.Ordinal)) { }
            }
    }

    private SqliteConnection OpenCatalog(bool initializing = false, bool fullMigrationEvidence = false, ContentInspectionBudget? inspection = null)
    {
        using var ownedInspection = inspection is null ? Inspection(default) : null;
        inspection ??= ownedInspection!;
        using var work = inspection.Inspect();
        inspection.Check();
        if (!initializing) FileSystemArtifactContentMigration.RequireCompletedWorkspace(_rootPath, _platform);
        // SQLite can touch rollback/WAL/SHM paths while opening or querying
        // journal mode. Reject unsafe paths before handing the catalog to SQLite.
        _platform.ValidatePrivatePermissions(_rootPath);
        ValidateProtocolPermissions(inspection);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = PathFor(Catalog), Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            inspection.Database(PathFor(Catalog));
            connection.Open();
            using var sqlInspection = inspection.InspectSql(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            if (!string.Equals((string?)command.ExecuteScalar(), "delete", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Content catalog requires DELETE journal mode.");
            command.CommandText = "PRAGMA synchronous=EXTRA; PRAGMA synchronous";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 3)
                throw new InvalidDataException("Content catalog requires EXTRA durability.");
            if (!initializing)
            {
                ContentCatalogSchema.Validate(connection);
                command.CommandText = "SELECT ArtifactId, Revision, Generation, Length, Owner FROM ContentState LIMIT 0; SELECT OperationId, Request, Receipt, AuditEventId, PublishedGeneration, MutationRevision, EnumerationSequence FROM ContentReceipts LIMIT 0";
                command.ExecuteNonQuery();
                Dictionary<(string Artifact, string Revision), MigrationOrigin> origins;
                using (var transaction = connection.BeginTransaction(deferred: true))
                {
                    origins = ValidateCatalogState(connection, transaction, inspection: inspection);
                    transaction.Commit();
                }
                if (fullMigrationEvidence)
                {
                    MigrationOrigins.VerifyEvidence(origins, _rootPath, _platform, _maxStoredBytes, inspection);
                    _verifiedOrigins = origins;
                }
            }
            inspection.Check();
            return connection;
        }
        catch (SqliteException) when (inspection.IsStopped)
        { connection.Dispose(); inspection.ThrowIfStopped(); throw; }
        catch (SqliteException error) when (error.SqliteErrorCode == SQLitePCL.raw.SQLITE_TOOBIG)
        { connection.Dispose(); throw new InvalidDataException("Content inspection exceeded its admitted row byte bound.", error); }
        catch { connection.Dispose(); throw; }
    }
    private Dictionary<(string Artifact, string Revision), MigrationOrigin> ValidateCatalogState(
        SqliteConnection connection, SqliteTransaction? transaction, bool foundationOnly = false, ContentInspectionBudget? inspection = null)
    {
        using var ownedInspection = inspection is null ? Inspection(default) : null;
        inspection ??= ownedInspection!;
        using var work = inspection.Inspect();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA quick_check";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Content catalog integrity check failed.");
        var origins = foundationOnly ? new Dictionary<(string Artifact, string Revision), MigrationOrigin>() :
            MigrationOrigins.Validate(connection, transaction, _rootPath, _platform, _maxStoredBytes, inspection);
        var receipts = new Dictionary<(string Artifact, string Revision), (ArtifactContentMutationReceipt Receipt, string? Generation)>();
        command.CommandText = "SELECT OperationId, Request, Receipt, AuditEventId, PublishedGeneration, MutationRevision, EnumerationSequence FROM ContentReceipts";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                inspection.Row();
                for (var column = 0; column < reader.FieldCount; column++)
                    if (!reader.IsDBNull(column) && reader.GetFieldType(column) == typeof(string)) inspection.Text(reader.GetString(column));
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
        if (receipts.Keys.Select(key => { inspection.Step(); return key.Revision; }).Intersect(origins.Keys.Select(key => { inspection.Step(); return key.Revision; }), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Content revision has contradictory lineage authorities.");
        foreach (var ((artifact, _), published) in receipts)
        {
            inspection.Step();
            if (published.Receipt.PriorRevision is { } previous && !receipts.ContainsKey((artifact, previous.Value)) && !origins.ContainsKey((artifact, previous.Value)))
                throw new InvalidDataException("Content mutation lineage is incomplete.");
        }
        var originRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in origins.Keys)
        {
            inspection.Step();
            if (!originRoots.TryAdd(key.Artifact, key.Revision))
                throw new InvalidDataException("Multiple bootstrap lineage origins.");
        }
        var visitedLineage = new HashSet<(string Artifact, string Revision)>();
        var stateArtifacts = new HashSet<string>(StringComparer.Ordinal);
        command.CommandText = "SELECT ArtifactId, Revision, Generation, Length, Owner FROM ContentState";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                inspection.Row();
                for (var column = 0; column < reader.FieldCount; column++)
                    if (!reader.IsDBNull(column) && reader.GetFieldType(column) == typeof(string)) inspection.Text(reader.GetString(column));
                var artifact = new ArtifactId(reader.GetString(0)).Value;
                if (!stateArtifacts.Add(artifact)) throw new InvalidDataException("Duplicate content state identity.");
                var revision = ArtifactContentIdentity.Validate(reader.GetString(1));
                var generation = reader.IsDBNull(2) ? null : reader.GetString(2);
                var length = reader.GetInt64(3);
                var owner = reader.IsDBNull(4) ? null : ArtifactContentIdentity.Validate(reader.GetString(4));
                var hasReceipt = receipts.TryGetValue((artifact, revision), out var published);
                var hasOrigin = origins.TryGetValue((artifact, revision), out var origin);
                if (hasReceipt == hasOrigin || length < 0 || generation is null && length != 0 ||
                    hasReceipt && (published.Receipt.OwnershipToken?.Value != owner || published.Generation != generation ||
                        (generation is null) != (published.Receipt.Outcome == ArtifactContentMutationOutcome.Deleted)) ||
                    hasOrigin && (owner is not null || origin!.Generation != generation || origin.Length != length))
                    throw new InvalidDataException("Content catalog state is contradictory.");
                if (generation is not null) ValidateGeneration(generation, length);
                ValidateLineage(artifact, revision, receipts, origins, originRoots.ContainsKey(artifact), visitedLineage, inspection);
            }
        }
        if (visitedLineage.Count != receipts.Count + origins.Count)
            throw new InvalidDataException("Content mutation lineage is branched or disconnected.");
        if (receipts.Keys.Concat(origins.Keys).Any(key => { inspection.Step(); return !stateArtifacts.Contains(key.Artifact); }))
            throw new InvalidDataException("Content mutation lineage has no current/tombstone state.");
        if (_admitted && (_verifiedOrigins is null || _verifiedOrigins.Count != origins.Count ||
            origins.Any(pair => { inspection.Step(); return !_verifiedOrigins.TryGetValue(pair.Key, out var verified) || verified != pair.Value; })))
            throw new InvalidDataException("Previously admitted migration provenance changed.");
        inspection.Check();
        return origins;
    }

    private static void ValidateLineage(string artifact, string current,
        Dictionary<(string Artifact, string Revision), (ArtifactContentMutationReceipt Receipt, string? Generation)> receipts,
        Dictionary<(string Artifact, string Revision), MigrationOrigin> origins, bool hasOrigin,
        HashSet<(string Artifact, string Revision)> visited, ContentInspectionBudget inspection)
    {
        var revision = current;
        while (true)
        {
            inspection.Step();
            if (!visited.Add((artifact, revision))) throw new InvalidDataException("Content mutation lineage is cyclic.");
            if (origins.ContainsKey((artifact, revision))) break;
            if (!receipts.TryGetValue((artifact, revision), out var node))
                throw new InvalidDataException("Content mutation lineage predecessor is missing.");
            if (node.Receipt.PriorRevision is { } prior) { revision = prior.Value; continue; }
            if (hasOrigin || node.Receipt.Outcome != ArtifactContentMutationOutcome.Created)
                throw new InvalidDataException("Content mutation lineage has an invalid root authority.");
            break;
        }

    }
    private sealed record State(string Revision, string? Generation, long Length, string? Owner);
    private static State? ReadState(SqliteConnection connection, SqliteTransaction? transaction, ArtifactId id)
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
