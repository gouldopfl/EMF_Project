using EMF.Inventory.Models;
using EMF.Inventory.Persistence;
using System.Text.Json;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;

public sealed class InventoryMigrationJournalTests
{
    [Fact]
    public async Task Identical_admission_is_idempotent_and_conflicting_admission_rejects()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync();
        var again = await f.Journal.AdmitAsync(s.Plan, "different-worker"); Assert.Equal(s.OwnerToken, again.OwnerToken); Assert.Equal(s.Version, again.Version); Assert.Equal(s.Plan.Items, again.Plan.Items);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.AdmitAsync(s.Plan with { WorkflowId = "other" }, s.OwnerToken));
        await f.RestartAsync(); var loaded = await f.Journal.LoadAsync(s.Plan.ParentId); Assert.Equal(s.Plan.Items[0], loaded!.Plan.Items[0]);
    }
    [Fact]
    public async Task Plan_is_ordered_immutable_and_frontier_requires_durable_proof()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.SqlAsync(Path.Combine(f.Source, "b.sqlite"), "CREATE TABLE other(id INTEGER);"); var s = await f.PlanAsync();
        Assert.Equal(new[] { 0, 1 }, s.Plan.Items.Select(x => x.Ordinal)); Assert.Equal(2, s.Plan.Items.Select(x => x.ChildOperationId).Distinct().Count());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.StartNextAsync(s, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.CompleteAsync(s));
        s = await f.ConfirmMetadataAsync(s); Assert.Equal(0, s.ConfirmedOrdinal);
        var stale = s; s = await f.ConfirmMetadataAsync(s); Assert.Equal(1, s.ConfirmedOrdinal);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.StartNextAsync(stale, 1)); Assert.Contains("journal version", ex.Message);
        s = await f.Journal.CompleteAsync(s); Assert.Equal(InventoryParentStatus.Completed, s.Status);
        await using var c = new SqliteConnection($"Data Source={Path.Combine(f.Root, "inventory-journal", "parent.sqlite")};Pooling=False"); await c.OpenAsync();
        using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE InventoryPlanItems SET Plan='{}'"; await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        cmd.CommandText = "DELETE FROM InventoryConfirmations WHERE Ordinal=0"; await cmd.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.LoadAsync(s.Plan.ParentId));
    }
    [Fact]
    public async Task Ownership_epoch_fences_previous_worker_separately_from_journal_version()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(); var next = await f.Journal.TakeOwnershipAsync(s, "next-worker");
        Assert.Equal(s.OwnerEpoch + 1, next.OwnerEpoch); Assert.Equal(s.Version + 1, next.Version);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.StartNextAsync(s, 0)); Assert.Contains("worker ownership", ex.Message);
        var version = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.StartNextAsync(next with { Version = s.Version }, 0)); Assert.Contains("journal version", version.Message);
    }
    [Theory]
    [InlineData(InventoryMode.MetadataOnly, InventoryConfirmationDisposition.ContentAdopted)]
    [InlineData(InventoryMode.ProtectedContent, InventoryConfirmationDisposition.MetadataPersisted)]
    public async Task Confirmation_dispositions_cannot_cross_modes(InventoryMode mode, InventoryConfirmationDisposition disposition)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(mode); var item = s.Plan.Items[0]; await f.Journal.StartNextAsync(s, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.ConfirmNextAsync(s, new(0, item.ChildOperationId, disposition, item.ArtifactId, "proof")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Snapshots.ReleaseAsync(item.Retained));
    }
    [Fact]
    public async Task Interrupted_pre_admission_preparation_is_quarantined_not_rebound()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.Snapshots.CaptureAsync(f.Context.OperationId!.Value.Value, f.SourceFile);
        await f.RestartAsync(); var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => f.PlanAsync()); Assert.Contains("requires review", ex.Message);
        Assert.Null(await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value));
    }
    [Fact]
    public async Task Journal_contains_binding_metadata_but_no_source_content_or_protection_credentials()
    {
        await using var f=await InventoryMigrationFixture.CreateAsync();
        var sensitive="synthetic-row-"+Guid.NewGuid().ToString("N");
        await f.SqlAsync(f.SourceFile,$"INSERT INTO evidence(name) VALUES('{sensitive}');");
        var state=await f.PlanAsync(InventoryMode.ProtectedContent);
        await using var c=new SqliteConnection($"Data Source={Path.Combine(f.Root,"inventory-journal","parent.sqlite")};Pooling=False");await c.OpenAsync();
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT Header FROM InventoryParents UNION ALL SELECT Plan FROM InventoryPlanItems UNION ALL SELECT Payload FROM InventoryRetention";
        using var reader=await cmd.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            var json=reader.GetString(0);Assert.DoesNotContain(sensitive,json);
            Assert.DoesNotContain("WrappedDataEncryptionKey",json);Assert.DoesNotContain("Ciphertext",json);
            Assert.DoesNotContain("synthetic-development-key",json);
        }
    }

    [Fact]
    public async Task Existing_version_one_journal_upgrades_idempotently_without_losing_evidence()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var state = await f.PlanAsync();
        var expected = await f.Journal.ReadRetentionAsync(state.Plan.Items[0].Retained.ObjectId);
        var path = Path.Combine(f.Root, "inventory-journal", "parent.sqlite");
        await f.SqlAsync(path, "DROP INDEX InventoryRetentionParentId;PRAGMA user_version=1;");
        var upgraded = new SqliteInventoryParentJournal(path, f.Limits);
        Assert.Equal(expected, await upgraded.ReadRetentionAsync(expected!.ObjectId));
        Assert.Equal(state.Plan.Items, (await upgraded.LoadAsync(state.Plan.ParentId))!.Plan.Items);
        _ = new SqliteInventoryParentJournal(path, f.Limits);
        await using var c = new SqliteConnection($"Data Source={path};Pooling=False"); await c.OpenAsync();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)(await cmd.ExecuteScalarAsync())!);
        cmd.CommandText = "PRAGMA index_info(InventoryRetentionParentId)";
        using var rows = await cmd.ExecuteReaderAsync(); var columns = new List<string>();
        while (await rows.ReadAsync()) columns.Add(rows.GetString(2));
        Assert.Equal(new[] { "Parent", "Id" }, columns);
    }

    [Fact]
    public async Task Large_unrelated_history_uses_bounded_parent_seek_and_preserves_history()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var path = Path.Combine(f.Root, "inventory-journal", "parent.sqlite");
        var history = new InventoryRetentionRecord("historical-parent", "history-template", "owner", "create", "release", InventoryRetentionStatus.Released);
        await SeedRetentionAsync(path, history, 4096);
        var current = new InventoryRetentionRecord("current-parent", "current-object", "owner", "create", "release", InventoryRetentionStatus.Reserved);
        await f.Journal.ReserveRetentionAsync(current);
        Assert.True(await f.Journal.HasRetainedPreparationAsync(current.ParentId));
        var sealing = current with { Status = InventoryRetentionStatus.Sealing, Length = 4096, Fingerprint = new string('A', 64) };
        await f.Journal.UpdateRetentionAsync(current, sealing);
        Assert.Equal(sealing, await f.Journal.ReadRetentionAsync(current.ObjectId));
        // A different parent's durable admission still progresses with the retained history intact.
        var unrelated = await f.PlanAsync(); Assert.Equal(-1, unrelated.ConfirmedOrdinal);
        await using var c = new SqliteConnection($"Data Source={path};Pooling=False"); await c.OpenAsync();
        using var cmd = c.CreateCommand();
        foreach (var projection in new[] { "Id", "Id,Payload" })
        {
            cmd.CommandText = $"EXPLAIN QUERY PLAN SELECT {projection} FROM InventoryRetention INDEXED BY InventoryRetentionParentId WHERE Parent=$parent ORDER BY Id LIMIT $limit";
            cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$parent", current.ParentId); cmd.Parameters.AddWithValue("$limit", f.Limits.MaximumPlanItems + 1);
            using var reader = await cmd.ExecuteReaderAsync(); var descriptions = new List<string>();
            while (await reader.ReadAsync()) descriptions.Add(reader.GetString(3));
            Assert.Contains(descriptions, x => x.Contains("SEARCH InventoryRetention", StringComparison.Ordinal) && x.Contains("InventoryRetentionParentId", StringComparison.Ordinal));
            Assert.DoesNotContain(descriptions, x => x.Contains("SCAN InventoryRetention", StringComparison.Ordinal) || x.Contains("TEMP B-TREE", StringComparison.Ordinal));
        }
        cmd.Parameters.Clear(); cmd.CommandText = "SELECT COUNT(*) FROM InventoryRetention WHERE Parent='historical-parent'";
        Assert.Equal(4096L, (long)(await cmd.ExecuteScalarAsync())!);
        cmd.CommandText = "SELECT Payload FROM InventoryRetention WHERE Id='history-0'";
        Assert.Equal(history with { ObjectId = "history-0" }, JsonSerializer.Deserialize<InventoryRetentionRecord>((string)(await cmd.ExecuteScalarAsync())!));
    }

    [Fact]
    public async Task Released_parent_history_still_consumes_identity_budget_and_is_not_discarded()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var path = Path.Combine(f.Root, "inventory-journal", "parent.sqlite");
        var history = new InventoryRetentionRecord("full-parent", "template", "owner", "create", "release", InventoryRetentionStatus.Released);
        await SeedRetentionAsync(path, history, f.Limits.MaximumPlanItems);
        Assert.True(await f.Journal.HasRetainedPreparationAsync(history.ParentId));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.ReserveRetentionAsync(history with { ObjectId = "new-object", Status = InventoryRetentionStatus.Reserved }));
        Assert.Null(await f.Journal.ReadRetentionAsync("new-object"));
        Assert.Equal(InventoryRetentionStatus.Released, (await f.Journal.ReadRetentionAsync("history-0"))!.Status);
    }

    [Fact]
    public async Task Excess_parent_history_rejects_before_reservation_or_sealing_changes()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var path = Path.Combine(f.Root, "inventory-journal", "parent.sqlite");
        var current = new InventoryRetentionRecord("damaged-parent", "current-object", "owner", "create", "release", InventoryRetentionStatus.Reserved);
        await f.Journal.ReserveRetentionAsync(current);
        await SeedRetentionAsync(path, current with { Status = InventoryRetentionStatus.Released }, f.Limits.MaximumPlanItems);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.HasRetainedPreparationAsync(current.ParentId));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.ReserveRetentionAsync(current with { ObjectId = "excess-new" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.UpdateRetentionAsync(current, current with { Status = InventoryRetentionStatus.Sealing, Length = 4096, Fingerprint = new string('A', 64) }));
        Assert.Equal(current, await f.Journal.ReadRetentionAsync(current.ObjectId));
        Assert.Null(await f.Journal.ReadRetentionAsync("excess-new"));
        await f.Journal.ReserveRetentionAsync(current with { ParentId = "unrelated-parent", ObjectId = "unrelated-object" });
        Assert.True(await f.Journal.HasRetainedPreparationAsync("unrelated-parent"));
    }

    [Fact]
    public async Task Bounded_sealing_rejects_oversized_history_payload_without_materializing_it()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var path = Path.Combine(f.Root, "inventory-journal", "parent.sqlite");
        var current = new InventoryRetentionRecord("damaged-parent", "current-object", "owner", "create", "release", InventoryRetentionStatus.Reserved);
        await f.Journal.ReserveRetentionAsync(current);
        await using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO InventoryRetention VALUES('bad-object',$parent,zeroblob($bytes))";
            cmd.Parameters.AddWithValue("$parent", current.ParentId); cmd.Parameters.AddWithValue("$bytes", f.Limits.MaximumPlanJsonBytes + 1); await cmd.ExecuteNonQueryAsync();
        }
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.UpdateRetentionAsync(current, current with { Status = InventoryRetentionStatus.Sealing, Length = 4096, Fingerprint = new string('A', 64) }));
        Assert.Contains("size limit", error.Message);
        Assert.Equal(current, await f.Journal.ReadRetentionAsync(current.ObjectId));
    }

    [Fact]
    public async Task Sealing_counts_active_bytes_but_preserves_released_evidence()
    {
        var limits = new InventoryProcessingLimits { MaximumRetainedBytes = 96L * 1024 * 1024 };
        await using var f = await InventoryMigrationFixture.CreateAsync(limits); var path = Path.Combine(f.Root, "inventory-journal", "parent.sqlite");
        var current = new InventoryRetentionRecord("budget-parent", "current-object", "owner", "create", "release", InventoryRetentionStatus.Reserved);
        await f.Journal.ReserveRetentionAsync(current);
        var other = current with { ObjectId = "history-0", Status = InventoryRetentionStatus.Sealing, Length = 60L * 1024 * 1024, Fingerprint = new string('B', 64) };
        await SeedRetentionAsync(path, other, 1);
        var sealing = current with { Status = InventoryRetentionStatus.Sealing, Length = 40L * 1024 * 1024, Fingerprint = new string('A', 64) };
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.UpdateRetentionAsync(current, sealing));
        Assert.Contains("aggregate byte budget", error.Message); Assert.Equal(current, await f.Journal.ReadRetentionAsync(current.ObjectId));
        await using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE InventoryRetention SET Payload=$payload WHERE Id='history-0'";
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(other with { Status = InventoryRetentionStatus.Released })); await cmd.ExecuteNonQueryAsync();
        }
        await f.Journal.UpdateRetentionAsync(current, sealing);
        Assert.Equal(sealing, await f.Journal.ReadRetentionAsync(current.ObjectId));
        Assert.Equal(other with { Status = InventoryRetentionStatus.Released }, await f.Journal.ReadRetentionAsync(other.ObjectId));
    }

    private static async Task SeedRetentionAsync(string path, InventoryRetentionRecord template, int count)
    {
        await using var c = new SqliteConnection($"Data Source={path};Pooling=False"); await c.OpenAsync();
        using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO InventoryRetention VALUES($id,$parent,$payload)";
        var id = cmd.Parameters.Add("$id", SqliteType.Text); cmd.Parameters.AddWithValue("$parent", template.ParentId);
        var payload = cmd.Parameters.Add("$payload", SqliteType.Text);
        for (var i = 0; i < count; i++)
        {
            id.Value = $"history-{i}"; payload.Value = JsonSerializer.Serialize(template with { ObjectId = (string)id.Value });
            await cmd.ExecuteNonQueryAsync();
        }
        tx.Commit();
    }

}
