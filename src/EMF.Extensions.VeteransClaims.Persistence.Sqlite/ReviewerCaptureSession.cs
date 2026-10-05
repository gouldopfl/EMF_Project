using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

// Owns exactly one read view. The trusted composition must supply a secured,
// versioned provider that honors cancellation and bounded termination.
public sealed class ReviewerCaptureSession : IReviewerCaptureSession
{
    private readonly SqliteConnection connection;
    private readonly SqliteTransaction transaction;
    private readonly CancellationTokenSource deadline;
    private readonly IVersionedArtifactContentStore source;
    private readonly OperationSnapshotId id;
    private readonly string[] members;
    private readonly ReviewerCaptureLimits limits;
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource disposalCancellation = new();
    private Task disposalCancellationCompletion = Task.CompletedTask;
    private bool used, disposed, cleanupStarted, disposalRequested;
    private TaskCompletionSource? captureCompletion;
    private Exception? idleCleanupFailure;
    private CancellationTokenRegistration idleRelease;
    private sealed class CaptureBudget(ReviewerCaptureLimits limits, CancellationToken token)
    {
        private int work;
        public bool Exhausted { get; private set; }
        public Action? Checkpoint { get; set; }
        public Exception? CheckpointFailure { get; private set; }
        public void Charge(int units, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); token.ThrowIfCancellationRequested();
            if (Exhausted || units > limits.MaximumWork - work)
            { Exhausted = true; throw new InvalidDataException("Capture work bound exceeded."); }
            work += units;
        }
        public int Progress(object unused)
        {
            try { Checkpoint?.Invoke(); }
            catch (Exception ex) { CheckpointFailure = ex; return 1; }
            if (token.IsCancellationRequested) return 1;
            if (100 > limits.MaximumWork - work) { Exhausted = true; return 1; }
            work += 100; return 0;
        }
    }
    private readonly CaptureBudget budget;
    private readonly CancellationTokenRegistration interrupt;
    private Action<string, SqliteConnection>? sqliteCheckpoint;
    // Test-only native progress/detachment barrier; never a production provider.
    internal Action<string, SqliteConnection>? SqliteCheckpoint
    {
        set { sqliteCheckpoint = value; budget.Checkpoint = () => value?.Invoke("Progress", connection); }
    }
    // Test-only observation after registration, before copying can fail.
    internal Action<string, byte[]>? OwnedPlaintextCheckpoint { get; set; }
    private long metadataBytes, sourceBytes;
    private readonly Func<string, CancellationToken, Task>? checkpoint;
    private ReviewerCaptureSession(SqliteConnection c, SqliteTransaction t, CancellationTokenSource d,
        IVersionedArtifactContentStore s, OperationSnapshotId i, string[] a, ReviewerCaptureLimits l, Func<string, CancellationToken, Task>? hook, CaptureBudget b, CancellationTokenRegistration registration)
    {
        (connection, transaction, deadline, source, id, members, limits) = (c,t,d,s,i,a,l);
        checkpoint = hook; budget = b; interrupt = registration;
        // A returned but never used session must not retain its read view indefinitely.
        // Active capture owns its cleanup; only idle sessions are closed by this callback.
        idleRelease = deadline.Token.Register(() =>
        {
            // Cancellation/timer callbacks cannot propagate disposal failures.
            lock (lifecycle)
            {
                if (!used)
                    try { DisposeOwnedResources(); }
                    catch (Exception ex) { idleCleanupFailure = ex; }
            }
        });
    }
    // Deterministic provider checkpoints; the callback must honor the same deadline.

    /// <summary>Acquires an existing read-only database view for the admitted proof profile.</summary>
    /// <param name="securedSource">Admission precondition: trusted composition supplies a secured,
    /// versioned provider with bounded source allocations and cancellation-cooperative termination.
    /// This versioned interface alone does not certify security or cancellation behavior.</param>
    public static async Task<ReviewerCaptureSession> OpenAsync(string databasePath, OperationSnapshotId id,
        IReadOnlyList<ArtifactId> members, IVersionedArtifactContentStore securedSource, ReviewerCaptureLimits limits,
        CancellationToken ct = default, Func<string, CancellationToken, Task>? checkpoint = null)
    {
        limits.Validate(); ArtifactContentIdentity.Validate(id.Value);
        ArgumentNullException.ThrowIfNull(securedSource);
        var ids = members.Select(x => x.Value).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0 || ids.Length > limits.MaximumMembers || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Capture member bound or identity mismatch.");
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(limits.Deadline); // Starts before acquisition; never reset.
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
        SqliteTransaction? t = null;
        var budget = new CaptureBudget(limits, deadline.Token);
        CancellationTokenRegistration registration = default;
        var progressInstalled = false;
        var transferred = false;
        try
        {
            if (checkpoint is not null) await checkpoint("Acquiring", deadline.Token);
            await c.OpenAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            raw.sqlite3_limit(c.Handle!, raw.SQLITE_LIMIT_LENGTH, (int)Math.Min(int.MaxValue, limits.MaximumMetadataBytes));
            raw.sqlite3_progress_handler(c.Handle!, 100, budget.Progress, null);
            progressInstalled = true;
            registration = deadline.Token.Register(() => raw.sqlite3_interrupt(c.Handle!));
            budget.Charge(100, deadline.Token);
            t = c.BeginTransaction(deferred: true);
            // Establish the view before returning the session.
            await using (var command = c.CreateCommand())
            {
                command.Transaction = t;
                command.CommandText = "SELECT Id FROM Artifacts LIMIT 1";
                await command.ExecuteScalarAsync(deadline.Token);
            }
            deadline.Token.ThrowIfCancellationRequested();
            budget.Charge(1,deadline.Token);
            var session = new ReviewerCaptureSession(c,t,deadline,securedSource,id,ids,limits,checkpoint,budget,registration);
            transferred = true; return session;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_TOOBIG ||
            (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT && (deadline.IsCancellationRequested || budget.Exhausted)))
        {
            if (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT && deadline.IsCancellationRequested)
                throw new OperationCanceledException("Capture deadline/cancellation interrupted SQLite.",ex,ct);
            throw new InvalidDataException("Capture SQLite work/row bound exceeded.",ex);
        }
        finally
        {
            if (!transferred)
            {
                try { registration.Dispose(); }
                finally
                {
                    try { if (progressInstalled) raw.sqlite3_progress_handler(c.Handle!,0,null,null); }
                    finally
                    {
                        try { t?.Dispose(); }
                        finally { try { await c.DisposeAsync(); } finally { deadline.Dispose(); } }
                    }
                }
            }
        }
    }

    private void Check(CancellationToken ct)
    {
        budget.Charge(1,ct);
    }
    private async Task<string> Query(string sql, string artifact, CancellationToken ct)
    {
        Check(ct);
        if (checkpoint is not null) await checkpoint("Metadata", ct);
        Check(ct); budget.Charge(100,ct);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = sql; command.Parameters.AddWithValue("$id", artifact);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            Check(ct);
            var row = new Dictionary<string, object?>();
            for (int n = 0; n < reader.FieldCount; n++)
            {
                Check(ct); var value = reader.IsDBNull(n) ? null : reader.GetValue(n);
                metadataBytes = checked(metadataBytes + JsonSerializer.SerializeToUtf8Bytes(value).Length + Encoding.UTF8.GetByteCount(reader.GetName(n)));
                if (metadataBytes > limits.MaximumMetadataBytes) throw new InvalidDataException("Capture metadata bound exceeded.");
                row.Add(reader.GetName(n), value);
            }
            rows.Add(row);
        }
        Check(ct); return JsonSerializer.Serialize(rows);
    }
    public async Task<ReviewerCapturedBundle> CaptureAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (used) throw new InvalidOperationException("Capture session is single use.");
            used = true;
            captureCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        CancellationTokenSource? linked = null;
        CancellationTokenRegistration captureInterrupt = default;
        var ct = cancellationToken;
        var ownedPlaintext = new List<byte[]>(members.Length);
        var completed = false;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken, disposalCancellation.Token);
            ct = linked.Token;
            captureInterrupt = ct.Register(() => raw.sqlite3_interrupt(connection.Handle!));
            var metadata = new List<ReviewerCapturedMetadata>(); var inputs = new List<ReviewerCapturedMember>();
            var content = new Dictionary<string, byte[]>(); var absences = new List<string>();
            foreach (var artifact in members)
            {
                var row = await Query("SELECT * FROM Artifacts WHERE Id=$id", artifact, ct);
                using var parsed = JsonDocument.Parse(row);
                if (parsed.RootElement.GetArrayLength() != 1) throw new InvalidDataException("Missing capture artifact.");
                var a = parsed.RootElement[0];
                if (a.GetProperty("ArtifactType").GetString() != "text/plain" || a.GetProperty("FingerprintAlgorithm").GetString() != "SHA-256")
                    throw new NotSupportedException("Capture proof requires fingerprinted UTF-8 text/plain.");
                var authority = await Query("SELECT ClassificationId,ClassificationRevision,IsAdopted FROM ArtifactMutationAuthority WHERE ArtifactId=$id", artifact, ct);
                using var auth = JsonDocument.Parse(authority);
                if (auth.RootElement.GetArrayLength() != 1 || auth.RootElement[0].GetProperty("IsAdopted").GetInt64() != 1)
                    throw new InvalidDataException("Capture requires adopted authority.");
                var provenance = await Query("SELECT * FROM Provenance WHERE ArtifactId=$id ORDER BY Id", artifact, ct);
                var relationships = await Query("SELECT * FROM Relationships WHERE SourceArtifactId=$id OR TargetArtifactId=$id ORDER BY Id", artifact, ct);
                // Supporting-artifact traversal belongs to the later complete reviewer profile.
                // Reject rather than silently omit a dependency outside this proof closure.
                using var rel = JsonDocument.Parse(relationships);
                foreach (var r in rel.RootElement.EnumerateArray())
                {
                    Check(ct);
                    if (!members.Contains(r.GetProperty("SourceArtifactId").GetString()) || !members.Contains(r.GetProperty("TargetArtifactId").GetString()))
                        throw new NotSupportedException("Relationship escapes admitted capture closure.");
                }
                if (provenance == "[]") absences.Add(artifact + ":provenance");
                if (relationships == "[]") absences.Add(artifact + ":relationships");
                var representation = JsonSerializer.Serialize(new { Artifact = row, Authority = authority, Provenance = provenance, Relationships = relationships });
                metadataBytes += Encoding.UTF8.GetByteCount(representation);
                if (metadataBytes > limits.MaximumMetadataBytes) throw new InvalidDataException("Capture metadata bound exceeded.");
                Check(ct);
                var snapshot = await source.ReadVersionedAsync(new ArtifactId(artifact), ct) ?? throw new InvalidDataException("Missing capture content.");
                // Provider memory has no ownership-transfer contract. Never clear it.
                Check(ct); sourceBytes = checked(sourceBytes + snapshot.Content.LongLength);
                if (sourceBytes > limits.MaximumSourceBytes) throw new InvalidDataException("Capture source byte bound exceeded.");
                if (checkpoint is not null) await checkpoint("Copying", ct);
                Check(ct);
                var bytes = new byte[snapshot.Content.Length];
                ownedPlaintext.Add(bytes); // Register before any copy, callback or await.
                OwnedPlaintextCheckpoint?.Invoke("Allocated", bytes);
                for (int offset = 0; offset < bytes.Length; offset += 81920)
                { Check(ct); snapshot.Content.AsSpan(offset, Math.Min(81920, bytes.Length-offset)).CopyTo(bytes.AsSpan(offset)); }
                OwnedPlaintextCheckpoint?.Invoke("Copied", bytes);
                var hash = ReviewerRetainedValidator.Hash(bytes);
                metadata.Add(new(artifact, representation, ReviewerRetainedValidator.Hash(Encoding.UTF8.GetBytes(representation))));
                inputs.Add(new(artifact, snapshot.Revision.Value, bytes.LongLength, hash, "SHA-256", a.GetProperty("FingerprintValue").GetString()!,
                    auth.RootElement[0].GetProperty("ClassificationId").GetString()!, auth.RootElement[0].GetProperty("ClassificationRevision").GetString()!));
                content.Add(artifact, bytes);
            }
            var result = new ReviewerCapturedBundle(new(1,id,ReviewerRetainedValidator.Profile,members,absences.ToArray(),metadata.ToArray(),inputs.ToArray()),content);
            Check(ct); completed = true; return result; // Interpretation/integrity validation follows live-resource disposal.
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_TOOBIG ||
            (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT && (ct.IsCancellationRequested || budget.Exhausted || budget.CheckpointFailure is not null)))
        {
            if (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT && ct.IsCancellationRequested) throw new OperationCanceledException("Capture cancelled during SQLite work.",ex,ct);
            if (budget.CheckpointFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(budget.CheckpointFailure).Throw();
            throw new InvalidDataException("Capture SQLite work/row bound exceeded.",ex);
        }
        finally
        {
            Exception? cleanupFailure = null;
            Task cancellationCompletion;
            lock (lifecycle)
            {
                cleanupStarted = true;
                cancellationCompletion = disposalCancellationCompletion;
            }
            try
            {
                // A disposal request runs callbacks outside the lifecycle lock.
                // Join them before removing registrations and disposing their signal.
                try { await cancellationCompletion.ConfigureAwait(false); }
                finally
                {
                    try { captureInterrupt.Dispose(); }
                    finally
                    {
                        try { linked?.Dispose(); }
                        finally
                        {
                            idleRelease.Dispose();
                            lock (lifecycle) { DisposeOwnedResources(); }
                        }
                    }
                }
            }
            catch (Exception ex) { cleanupFailure = ex; completed = false; throw; }
            finally
            {
                // Successful return transfers these buffers to the bundle owner.
                if (!completed)
                    foreach (var bytes in ownedPlaintext)
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
                if (cleanupFailure is null) captureCompletion!.TrySetResult();
                else captureCompletion!.TrySetException(cleanupFailure);
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        // Active capture exclusively tears down its connection. Disposal requests
        // cancellation through our own signal, then joins capture-owned cleanup.
        lock (lifecycle)
        {
            if (captureCompletion is not null)
            {
                RequestCaptureDisposal();
                return new(captureCompletion.Task);
            }
        }
        // Unregister outside the lock: a running idle callback may need that lock.
        idleRelease.Dispose();
        lock (lifecycle)
        {
            // Capture may have started while the idle registration was detached.
            if (captureCompletion is not null)
            {
                RequestCaptureDisposal();
                return new(captureCompletion.Task);
            }
            DisposeOwnedResources();
            if (idleCleanupFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(idleCleanupFailure).Throw();
        }
        return ValueTask.CompletedTask;
    }
    // Called under lifecycle. Cleanup has already stopped capture work, so a
    // request arriving during teardown only joins it. Schedule cancellation once
    // to preserve the task representing all running cancellation callbacks.
    private void RequestCaptureDisposal()
    {
        if (cleanupStarted || disposed || disposalRequested) return;
        disposalRequested = true;
        disposalCancellationCompletion = disposalCancellation.CancelAsync();
    }
    private void DisposeOwnedResources()
    {
        if (disposed) return;
        disposed = true;
        try { interrupt.Dispose(); }
        finally
        {
            try
            {
                raw.sqlite3_progress_handler(connection.Handle!,0,null,null);
                sqliteCheckpoint?.Invoke("Detached", connection);
            }
            finally
            {
                try { transaction.Dispose(); }
                finally
                {
                    try { connection.Dispose(); }
                    finally { try { deadline.Dispose(); } finally { disposalCancellation.Dispose(); } }
                }
            }
        }
    }
}
