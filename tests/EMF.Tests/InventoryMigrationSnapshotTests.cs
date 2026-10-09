using System.Security.Cryptography;
using EMF.Inventory.Models;
using EMF.Inventory.Providers;
using EMF.Inventory.Storage;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;

public sealed class InventoryMigrationSnapshotTests
{
    [Fact]
    public async Task Stepped_backup_includes_WAL_and_same_snapshot_drives_hash_and_inspection_after_source_removal()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        await using var live = new SqliteConnection($"Data Source={f.SourceFile};Pooling=False"); await live.OpenAsync(); using var cmd = live.CreateCommand(); cmd.CommandText = "PRAGMA journal_mode=WAL;PRAGMA wal_autocheckpoint=0;INSERT INTO evidence(name) VALUES('wal');"; await cmd.ExecuteNonQueryAsync(); Assert.True(File.Exists(f.SourceFile + "-wal"));
        var steps = 0; await f.RestartAsync((stage, ct) => { steps++; return Task.CompletedTask; }); var s = await f.PlanAsync(); Assert.True(steps > 0);
        await live.CloseAsync(); File.Delete(f.SourceFile); await f.RestartAsync();
        var result = await f.Service.ReadAsync(s.Plan.Items[0], default); Assert.Equal(2, Assert.Single(result.Inventory!.Tables).RowCount);
        var bytes = await f.Service.ReadContentAsync(s.Plan.Items[0], default); Assert.Equal(s.Plan.Items[0].Retained.Fingerprint, Convert.ToHexString(SHA256.HashData(bytes))); CryptographicOperations.ZeroMemory(bytes);
        Assert.Empty(Directory.EnumerateDirectories(f.Workspace.Root, "work-*"));
    }
    [Fact]
    public async Task Cancellation_between_steps_finishes_backup_and_cleans_owned_plaintext()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); using var cancel = new CancellationTokenSource(); await f.RestartAsync((stage, ct) => { cancel.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Snapshots.CaptureAsync("cancel-parent", f.SourceFile, cancel.Token));
        Assert.Empty(Directory.EnumerateDirectories(f.Workspace.Root, "work-*")); await f.SqlAsync(f.SourceFile, "INSERT INTO evidence(name) VALUES('after-cancel');");
    }
    [Fact]
    public async Task Positive_page_attempt_budget_rejects_without_sealing()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(new() { BackupBatchPages = 1, MaximumPageAttempts = 1 });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Snapshots.CaptureAsync("work-parent", f.SourceFile));
        Assert.Equal(0, f.Ingestion.Encryption.Encryptions);
    }
    [Fact]
    public async Task Busy_retry_budget_is_bounded()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(new() { MaximumBusyRetries = 0 });
        await using var blocker = new SqliteConnection($"Data Source={f.SourceFile};Pooling=False;Default Timeout=0"); await blocker.OpenAsync();
        var locked = false;
        await f.RestartAsync(async (stage, ct) => { if (!locked) { using var cmd = blocker.CreateCommand(); cmd.CommandText = "BEGIN EXCLUSIVE"; await cmd.ExecuteNonQueryAsync(ct); locked = true; } });
        try { var e = await Assert.ThrowsAsync<InvalidDataException>(() => f.Snapshots.CaptureAsync("busy-parent", f.SourceFile)); Assert.Contains("busy budget", e.Message); }
        finally { if (locked) { using var cmd = blocker.CreateCommand(); cmd.CommandText = "ROLLBACK"; await cmd.ExecuteNonQueryAsync(); } }
    }
    [Fact]
    public async Task Oversized_snapshot_rejects_before_protection_allocation()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(new() { MaximumSnapshotBytes = 4096 });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Snapshots.CaptureAsync("size-parent", f.SourceFile)); Assert.Equal(0, f.Ingestion.Encryption.Encryptions);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(67108865)]
    public void Invalid_plaintext_profile_is_configuration_error(long value) => Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryProcessingLimits { MaximumPlaintextBytes = value }.Validate());
    [Fact]
    public async Task Wrong_binding_fields_and_missing_retained_object_fail_closed()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(); var b = s.Plan.Items[0].Retained;
        foreach (var wrong in new[] { b with { Revision = "wrong" }, b with { Length = b.Length + 1 }, b with { Fingerprint = new string('0', 64) }, b with { OwnerToken = "wrong" } })
            await Assert.ThrowsAsync<InvalidDataException>(() => f.Snapshots.MaterializeAsync(wrong));
        await f.Physical.DeleteAsync(new("inventory-retained-" + b.ObjectId));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Snapshots.MaterializeAsync(b));
    }
    [Fact]
    public async Task Restart_receipts_and_confirmation_authorized_release_are_durable()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(); var b = s.Plan.Items[0].Retained; await f.RestartAsync();
        Assert.NotNull(await f.Physical.GetMutationOutcomeAsync(new(b.CreateOperationId)));
        await using (var lease = await f.Snapshots.MaterializeAsync(b)) Assert.True(File.Exists(lease.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Snapshots.ReleaseAsync(b));
        s = await f.ConfirmMetadataAsync(s); await f.Journal.AuthorizeReleaseAsync(s, 0); await f.Snapshots.ReleaseAsync(b); await f.RestartAsync(); await f.Snapshots.ReleaseAsync(b);
        Assert.NotNull(await f.Physical.GetMutationOutcomeAsync(new(b.ReleaseOperationId))); Assert.Equal(InventoryRetentionStatus.Released, (await f.Journal.ReadRetentionAsync(b.ObjectId))!.Status);
    }
    [Fact]
    public async Task Owned_abandoned_materialization_is_recovered_after_restart()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(); var lease = await f.Snapshots.MaterializeAsync(s.Plan.Items[0].Retained); Assert.True(File.Exists(lease.Path));
        await f.RestartAsync(); await f.Workspace.RecoverAsync(f.Journal, 10, default); Assert.False(File.Exists(lease.Path));
    }
    [Fact]
    public void Repository_and_Windows_plaintext_workspaces_are_rejected()
    {
        Assert.ThrowsAny<Exception>(() => new LinuxInventorySnapshotWorkspace("/home/michael/EMF_Project/tmp-inventory"));
        Assert.ThrowsAny<Exception>(() => new LinuxInventorySnapshotWorkspace("/home/michael/EMF-from-Windows/tmp-inventory"));
    }
    [Fact]
    public async Task Table_and_column_metadata_limits_reject_without_truncation()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.SqlAsync(f.SourceFile, "CREATE TABLE second(id INTEGER);");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteInventoryProvider(new() { MaximumTables = 1 }).CreateInventoryAsync(f.SourceFile));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteInventoryProvider(new() { MaximumColumns = 1 }).CreateInventoryAsync(f.SourceFile));
    }
    [Fact]
    public async Task Aggregate_retention_quota_rejects_before_encrypting_excess_object()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(new() { MaximumSnapshotBytes = 16384, MaximumPlaintextBytes = 16384, MaximumProtectedBytes = 32768, MaximumRetainedBytes = 32768 });
        for (var n = 0; n < 4; n++) await f.Snapshots.CaptureAsync("quota-parent", f.SourceFile);
        var count = f.Ingestion.Encryption.Encryptions;
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => f.Snapshots.CaptureAsync("quota-parent", f.SourceFile)); Assert.Contains("aggregate byte budget", e.Message); Assert.Equal(count, f.Ingestion.Encryption.Encryptions);
    }
    [Fact]
    public async Task Journal_is_not_locked_while_detached_backup_waits()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.RestartAsync(async (stage, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct).ConfigureAwait(false); }); var capture = f.Snapshots.CaptureAsync("detached-parent", f.SourceFile);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await f.Journal.ReserveRetentionAsync(new("unrelated-parent", "unrelated-object", "owner", "create-op", "delete-op", InventoryRetentionStatus.Reserved)).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.TrySetResult(); }
        await capture;
    }
    [Fact]
    public async Task Protected_representation_ceiling_rejects_before_physical_publication()
    {
        await using var f=await InventoryMigrationFixture.CreateAsync(new(){MaximumSnapshotBytes=8192,MaximumPlaintextBytes=8192,MaximumProtectedBytes=9000,MaximumRetainedBytes=9000});
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Snapshots.CaptureAsync("representation-parent",f.SourceFile));
        Assert.Null(await f.Journal.LoadAsync("representation-parent"));
        await using var c=new SqliteConnection($"Data Source={Path.Combine(f.Root,"inventory-journal","parent.sqlite")};Pooling=False");await c.OpenAsync();
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT Id FROM InventoryRetention WHERE Parent='representation-parent'";
        var id=(string)(await cmd.ExecuteScalarAsync())!;var retained=(await f.Journal.ReadRetentionAsync(id))!;
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(new(retained.CreateOperationId)));
        Assert.Null(await f.Physical.ReadAsync(new("inventory-retained-"+id)));
    }
    [Fact]
    public async Task Actual_retention_receipt_rejects_wrong_release_owner_without_deleting()
    {
        await using var f=await InventoryMigrationFixture.CreateAsync();var state=await f.PlanAsync();var b=state.Plan.Items[0].Retained;
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Protection.ReleaseAsync(b with{OwnerToken="wrong-owner"}));
        await using var lease=await f.Snapshots.MaterializeAsync(b);Assert.True(File.Exists(lease.Path));
    }

    [Fact]
    public async Task Failed_ephemeral_release_retries_and_success_is_idempotent()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var attempts = 0;
        var workspace = new LinuxInventorySnapshotWorkspace(f.Workspace.Root, _ => ++attempts == 1
            ? Task.FromException(new IOException("Injected remove failure.")) : Task.CompletedTask);
        var store = new SqliteInventoryRetainedSnapshotStore(f.Journal, f.Protection, workspace, f.Limits);
        var lease = await store.CaptureEphemeralAsync(f.SourceFile);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        Assert.True(File.Exists(lease.Path)); Assert.Single(Directory.EnumerateFiles(workspace.Root, "cleanup-work-*.json"));
        await lease.DisposeAsync(); Assert.False(File.Exists(lease.Path)); Assert.Equal(2, attempts);
        await lease.DisposeAsync(); Assert.Equal(2, attempts);
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, "work-*"));
        Assert.Empty(Directory.EnumerateFiles(workspace.Root, "cleanup-work-*.json"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_disposal_joins_one_attempt_and_observes_its_success_or_failure(bool fail)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var attempts = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new LinuxInventorySnapshotWorkspace(f.Workspace.Root, async _ =>
        {
            if (++attempts == 1)
            {
                entered.TrySetResult(); await proceed.Task.ConfigureAwait(false);
                if (fail) throw new IOException("Injected first attempt failure.");
            }
        });
        var store = new SqliteInventoryRetainedSnapshotStore(f.Journal, f.Protection, workspace, f.Limits);
        var lease = await store.CaptureEphemeralAsync(f.SourceFile);
        var first = lease.DisposeAsync().AsTask(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = lease.DisposeAsync().AsTask(); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        Assert.Equal(1, attempts); proceed.TrySetResult();
        if (fail)
        {
            await Assert.ThrowsAsync<IOException>(() => first); await Assert.ThrowsAsync<IOException>(() => second);
            Assert.Equal(1, attempts); Assert.True(File.Exists(lease.Path));
            await lease.DisposeAsync(); Assert.Equal(2, attempts);
        }
        else { await Task.WhenAll(first, second); Assert.Equal(1, attempts); }
        Assert.False(File.Exists(lease.Path));
        await lease.DisposeAsync(); Assert.Equal(fail ? 2 : 1, attempts);
    }
    [Fact]
    public async Task Fresh_recovery_removes_failed_ephemeral_cleanup_without_parent_or_retention_state()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var workspace = new LinuxInventorySnapshotWorkspace(f.Workspace.Root, _ => Task.FromException(new IOException("Injected cleanup outage.")));
        var store = new SqliteInventoryRetainedSnapshotStore(f.Journal, f.Protection, workspace, f.Limits);
        var lease = await store.CaptureEphemeralAsync(f.SourceFile);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        using (var owner = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(lease.Path)!, "owner.json"))))
        {
            Assert.True(owner.RootElement.GetProperty("Ephemeral").GetBoolean());
            Assert.Null(await f.Journal.LoadAsync(owner.RootElement.GetProperty("ParentId").GetString()!));
            Assert.Null(await f.Journal.ReadRetentionAsync(owner.RootElement.GetProperty("ObjectId").GetString()!));
        }
        await f.RestartAsync(); // Fresh journal/workspace/protection/providers; only backing files survive.
        await f.Workspace.RecoverAsync(f.Journal, 1024);
        Assert.False(File.Exists(lease.Path)); Assert.Empty(Directory.EnumerateDirectories(f.Workspace.Root, "work-*"));
        await lease.DisposeAsync(); // Recovery already completed physical removal; retry is harmless.
    }
    [Fact]
    public async Task Recovery_skips_a_live_ephemeral_input_until_disposal()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        await using var lease = await f.Snapshots.CaptureEphemeralAsync(f.SourceFile);
        var recovery = new LinuxInventorySnapshotWorkspace(f.Workspace.Root);
        await recovery.RecoverAsync(f.Journal, 1024);
        Assert.True(File.Exists(lease.Path));
        await lease.DisposeAsync(); Assert.False(File.Exists(lease.Path));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_cleanup_record_survives_owner_unlink_or_directory_removal(bool directoryRemoved)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var workspace = new LinuxInventorySnapshotWorkspace(f.Workspace.Root, _ => Task.FromException(new IOException("Injected cleanup interruption.")));
        var store = new SqliteInventoryRetainedSnapshotStore(f.Journal, f.Protection, workspace, f.Limits);
        var lease = await store.CaptureEphemeralAsync(f.SourceFile);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        var dir = Path.GetDirectoryName(lease.Path)!;
        // Simulate the two later unlink interruption points after durable external intent.
        File.Delete(lease.Path); File.Delete(Path.Combine(dir, "owner.json"));
        if (directoryRemoved) Directory.Delete(dir);
        LinuxInventorySnapshotWorkspace.FlushDirectory(workspace.Root);
        await f.RestartAsync(); await f.Workspace.RecoverAsync(f.Journal, 1024);
        Assert.False(Directory.Exists(dir)); Assert.Empty(Directory.EnumerateFiles(f.Workspace.Root, "cleanup-work-*.json"));
        await lease.DisposeAsync();
    }
    [Theory]
    [InlineData("malformed")]
    [InlineData("unknown-version")]
    [InlineData("excess-entries")]
    [InlineData("symlink")]
    public async Task Recovery_rejects_unknown_or_unsafe_ephemeral_workspace_without_deleting(string damage)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var path = f.Workspace.Create("safety-parent", "safety-object", ephemeral: true);
        var dir = Path.GetDirectoryName(path)!;
        using (var file = f.Workspace.CreateFile(path)) file.WriteByte(7);
        var marker = Path.Combine(dir, "owner.json");
        if (damage == "malformed") await File.WriteAllTextAsync(marker, "{invalid");
        else if (damage == "unknown-version") await File.WriteAllTextAsync(marker, "{\"ParentId\":\"safety-parent\",\"ObjectId\":\"safety-object\",\"Ephemeral\":true,\"Version\":99}");
        else if (damage == "excess-entries")
            for (var i = 0; i < 8; i++) { using var file = f.Workspace.CreateFile(Path.Combine(dir, $"unknown-{i}")); }
        else { File.Delete(path); File.CreateSymbolicLink(path, f.SourceFile); }
        var recovery = new LinuxInventorySnapshotWorkspace(f.Workspace.Root);
        await Assert.ThrowsAnyAsync<Exception>(() => recovery.RecoverAsync(f.Journal, 1024));
        Assert.True(Directory.Exists(dir)); Assert.True(File.Exists(marker)); Assert.True(File.Exists(path));
        Assert.True(File.Exists(f.SourceFile));
    }
    [Fact]
    public async Task Recovery_preserves_unknown_durable_workspace_without_retained_authority()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var path = f.Workspace.Create("unknown-durable-parent", "unknown-durable-object");
        using (var file = f.Workspace.CreateFile(path)) file.WriteByte(7);
        var recovery = new LinuxInventorySnapshotWorkspace(f.Workspace.Root);
        await recovery.RecoverAsync(f.Journal, 1024);
        Assert.True(File.Exists(path)); Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "owner.json")));
        Assert.Null(await f.Journal.ReadRetentionAsync("unknown-durable-object"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_record_never_treats_replaced_workdir_as_absent(bool symbolicLink)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var workspace = new LinuxInventorySnapshotWorkspace(f.Workspace.Root, _ => Task.FromException(new IOException("Injected cleanup interruption.")));
        var store = new SqliteInventoryRetainedSnapshotStore(f.Journal, f.Protection, workspace, f.Limits);
        var lease = await store.CaptureEphemeralAsync(f.SourceFile);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        var dir = Path.GetDirectoryName(lease.Path)!;
        File.Delete(lease.Path); File.Delete(Path.Combine(dir, "owner.json")); Directory.Delete(dir);
        if (symbolicLink) Directory.CreateSymbolicLink(dir, Path.Combine(f.Root, "missing-target"));
        else { using var file = workspace.CreateFile(dir); file.WriteByte(1); }
        var recovery = new LinuxInventorySnapshotWorkspace(f.Workspace.Root);
        await Assert.ThrowsAsync<IOException>(() => recovery.RecoverAsync(f.Journal, 1024));
        Assert.Single(Directory.EnumerateFiles(workspace.Root, "cleanup-work-*.json"));
        if (symbolicLink) Assert.NotNull(new DirectoryInfo(dir).LinkTarget); else Assert.True(File.Exists(dir));
    }

}
