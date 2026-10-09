using System.Diagnostics;
using System.Security.Cryptography;
using EMF.Inventory.Contracts;
using EMF.Inventory.Models;
using EMF.Inventory.Storage;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace EMF.Inventory.Providers;

public sealed class SqliteInventoryRetainedSnapshotStore : IInventoryRetainedSnapshotStore, IInventoryEphemeralSnapshotStore
{
    private readonly IInventoryParentJournal _journal;
    private readonly IInventoryProtectedSnapshotStorage _protected;
    private readonly LinuxInventorySnapshotWorkspace _workspace;
    private readonly InventoryProcessingLimits _limits;
    private readonly Func<string, CancellationToken, Task>? _checkpoint;
    public SqliteInventoryRetainedSnapshotStore(IInventoryParentJournal journal, IInventoryProtectedSnapshotStorage protectedStorage,
        LinuxInventorySnapshotWorkspace workspace, InventoryProcessingLimits? limits = null, Func<string, CancellationToken, Task>? checkpoint = null)
    { (_journal, _protected, _workspace) = (journal, protectedStorage, workspace); _limits = limits ?? new(); _limits.Validate(); _checkpoint = checkpoint; }

    public async Task<InventoryRetainedBinding> CaptureAsync(string parentId, string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Inventory source is missing.", sourcePath);
        var request = new InventoryRetentionRecord(parentId, InventoryIdentity.New(), InventoryIdentity.New(), InventoryIdentity.New(), InventoryIdentity.New(), InventoryRetentionStatus.Reserved);
        await _journal.ReserveRetentionAsync(request, ct);
        var path = _workspace.Create(parentId, request.ObjectId);
        bool ambiguous = false;
        try
        {
            await BackupAsync(sourcePath, path, ct).ConfigureAwait(false);
            LinuxInventorySnapshotWorkspace.Validate(path, false);
            var length = new FileInfo(path).Length;
            if (length <= 0 || length > _limits.MaximumSnapshotBytes) throw new InvalidDataException("Inventory snapshot size limit.");
            string hash; await using (var stream = File.OpenRead(path)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            var sealing = request with { Status = InventoryRetentionStatus.Sealing, Fingerprint = hash, Length = length };
            await _journal.UpdateRetentionAsync(request, sealing, ct); ambiguous = true;
            var binding = await _protected.SealAsync(sealing, path, ct).ConfigureAwait(false);
            await _journal.UpdateRetentionAsync(sealing, sealing with { Status = InventoryRetentionStatus.Sealed, Binding = binding }, ct);
            ambiguous = false; return binding;
        }
        finally { if (!ambiguous) _workspace.Remove(path); }
    }
    public async Task<InventorySnapshotLease> CaptureEphemeralAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Inventory source is missing.", sourcePath);
        var parent = InventoryIdentity.New();
        var execution = await _workspace.AcquireAsync(parent, ct).ConfigureAwait(false);
        InventorySnapshotLease? lease = null;
        try
        {
            var path = _workspace.Create(parent, InventoryIdentity.New(), ephemeral: true);
            lease = new(_workspace, path, parent, execution);
            await BackupAsync(sourcePath, path, ct).ConfigureAwait(false);
            LinuxInventorySnapshotWorkspace.Validate(path, false);
            var length = new FileInfo(path).Length;
            if (length <= 0 || length > _limits.MaximumSnapshotBytes) throw new InvalidDataException("Inventory snapshot size limit.");
            return lease;
        }
        catch (Exception operationFailure)
        {
            try
            {
                if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
                else await execution.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure) { throw new AggregateException("Inventory snapshot preparation and cleanup failed.", operationFailure, cleanupFailure); }
            throw;
        }
    }
    private async Task BackupAsync(string sourcePath, string destination, CancellationToken ct)
    {
        using (var file = _workspace.CreateFile(destination)) file.Flush(true);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 0 }.ToString());
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 0 }.ToString());
        await source.OpenAsync(ct); await target.OpenAsync(ct);
        raw.sqlite3_busy_timeout(source.Handle!, 0); raw.sqlite3_busy_timeout(target.Handle!, 0);
        long Scalar(string sql) { using var cmd = source.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
        var pageSize = Scalar("PRAGMA page_size");
        if (checked(Scalar("PRAGMA page_count") * pageSize) > _limits.MaximumSnapshotBytes) throw new InvalidDataException("Inventory snapshot pre-admission size limit.");
        using var backup = raw.sqlite3_backup_init(target.Handle!, "main", source.Handle!, "main");
        if (backup.IsInvalid) throw new InvalidDataException("SQLite backup initialization failed.");
        var timer = Stopwatch.StartNew(); long work = 0; int busy = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                work = checked(work + _limits.BackupBatchPages);
                if (work > _limits.MaximumPageAttempts || timer.Elapsed > _limits.ExecutionBudget) throw new InvalidDataException("Inventory backup work budget exhausted.");
                if (_checkpoint is not null) await _checkpoint("BeforeBackupStep", ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                int code = raw.sqlite3_backup_step(backup, _limits.BackupBatchPages);
                var pages = raw.sqlite3_backup_pagecount(backup);
                if (pages < 0 || checked((long)pages * pageSize) > _limits.MaximumSnapshotBytes || new FileInfo(destination).Length > _limits.MaximumSnapshotBytes)
                    throw new InvalidDataException("Inventory backup observed size limit.");
                ct.ThrowIfCancellationRequested();
                if (code == raw.SQLITE_DONE)
                { if (raw.sqlite3_backup_finish(backup) != raw.SQLITE_OK) throw new InvalidDataException("SQLite backup finish failed."); break; }
                if (code is raw.SQLITE_BUSY or raw.SQLITE_LOCKED)
                { if (++busy > _limits.MaximumBusyRetries) throw new InvalidDataException("Inventory backup busy budget exhausted."); await Task.Delay(10, ct).ConfigureAwait(false); }
                else if (code != raw.SQLITE_OK) throw new InvalidDataException("SQLite backup failed: " + code);
            }
        }
        finally { if (!backup.IsInvalid) raw.sqlite3_backup_finish(backup); }
        await target.CloseAsync(); await source.CloseAsync();
        using (var file = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
        LinuxInventorySnapshotWorkspace.FlushDirectory(Path.GetDirectoryName(destination)!);
    }
    public async Task<InventorySnapshotLease> MaterializeAsync(InventoryRetainedBinding binding, CancellationToken ct = default)
    {
        var r = await _journal.ReadRetentionAsync(binding.ObjectId, ct);
        if (r?.Status != InventoryRetentionStatus.Sealed || r.Binding != binding) throw new InvalidDataException("Retained snapshot is not sealed/available.");
        var path = _workspace.Create(r.ParentId, binding.ObjectId);
        try { await _protected.MaterializeAsync(binding, path, ct).ConfigureAwait(false); return new(_workspace, path); }
        catch { _workspace.Remove(path); throw; }
    }
    public async Task ReleaseAsync(InventoryRetainedBinding binding, CancellationToken ct = default)
    {
        var r = await _journal.ReadRetentionAsync(binding.ObjectId, ct);
        if (r?.Binding != binding || r.Status is not (InventoryRetentionStatus.ReleasePending or InventoryRetentionStatus.Released)) throw new InvalidOperationException("Retained release is not authorized.");
        await _protected.ReleaseAsync(binding, ct).ConfigureAwait(false);
        if (r.Status == InventoryRetentionStatus.ReleasePending) await _journal.UpdateRetentionAsync(r, r with { Status = InventoryRetentionStatus.Released }, ct);
    }
}
