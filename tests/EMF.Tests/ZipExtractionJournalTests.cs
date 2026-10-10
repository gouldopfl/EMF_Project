using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ZipExtractionJournalTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zip-journal-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root,"evidence.db");
        public Clock Clock { get; } = new();
        public SqliteZipExtractionJournal Journal => new(Path,Clock);
        public ZipParentBinding Binding { get; } = new("parent-operation","parent-artifact",
            new("retained-parent","revision-1",new string('A',64),100),new string('B',64));
        public static async Task<Fixture> CreateAsync(bool initialize=true)
        {
            var f=new Fixture();Directory.CreateDirectory(f.Root);
            if(!OperatingSystem.IsWindows())System.IO.File.SetUnixFileMode(f.Root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            await new SqliteEvidenceRepository(f.Path).InitializeAsync();
            if(!OperatingSystem.IsWindows())System.IO.File.SetUnixFileMode(f.Path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            if(initialize)await f.Journal.InitializeAsync();return f;
        }
        public async Task<ZipParentSnapshot> ParentAsync(bool reservePreflight=true)
        {
            await Journal.CreateAsync(Binding);var p=await Journal.ClaimAsync(Binding.OperationId,"worker-1",TimeSpan.FromMinutes(5));
            return reservePreflight ? await Journal.ReserveAsync(p.Fence,new("admission-preflight",ZipWorkKind.Preflight,null,Binding.Input.Length+65539)) : p;
        }
        public async Task SqlAsync(string sql)
        {
            await using var c=new SqliteConnection("Data Source="+Path);await c.OpenAsync();
            using var q=c.CreateCommand();q.CommandText=sql;await q.ExecuteNonQueryAsync();
        }
        public async Task<long> ScalarAsync(string sql)
        {
            await using var c=new SqliteConnection("Data Source="+Path);await c.OpenAsync();
            using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(await q.ExecuteScalarAsync());
        }
        public ValueTask DisposeAsync()
        {
            using var connection=new SqliteConnection("Data Source="+Path);
            SqliteConnection.ClearPool(connection);Directory.Delete(Root,true);return ValueTask.CompletedTask;
        }
    }
    private static ZipPlan Plan(params ZipEntryPlan[] entries)
    {
        const string receipt="{\"PreflightVersion\":1}";
        return new(SqliteZipExtractionJournal.PlanHash(receipt,entries),receipt,entries);
    }
    private static ZipEntryPlan File(int central=0,int file=0,string name="same.txt",long length=10) =>
        new(central,file,name,name,5,length,123,false,false,"child-"+file,"artifact-"+file);

    [Fact]
    public async Task Migration_is_additive_idempotent_and_existing_evidence_remains_usable()
    {
        await using var f=await Fixture.CreateAsync(false);
        var before=await f.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
        await f.Journal.InitializeAsync();await f.Journal.InitializeAsync();
        Assert.Equal(before+12,await f.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"));
        Assert.Equal(5,await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('ArtifactIngestionSchema','WorkflowCheckpoints')"));
        await new SqliteEvidenceRepository(f.Path).InitializeAsync();
    }
    [Fact]
    public async Task Failed_migration_rolls_back_all_zip_objects()
    {
        await using var f=await Fixture.CreateAsync(false);
        await f.SqlAsync("CREATE TABLE ZipExtractionEntries(Unexpected TEXT)");
        await Assert.ThrowsAsync<SqliteException>(()=>f.Journal.InitializeAsync());
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('ZipExtractionSchema','ZipExtractionParents')"));
        Assert.Equal(3,await f.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Artifacts','Relationships','Provenance')"));
        await f.SqlAsync("DROP TABLE ZipExtractionEntries");
        await f.Journal.InitializeAsync();
        Assert.Equal(5,await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionParents"));
    }
    [Fact]
    public async Task Missing_evidence_database_is_not_created()
    {
        var root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            if(!OperatingSystem.IsWindows())System.IO.File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            var path=System.IO.Path.Combine(root,"missing.db");
            await Assert.ThrowsAnyAsync<Exception>(()=>new SqliteZipExtractionJournal(path).InitializeAsync());
            Assert.False(System.IO.File.Exists(path));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task Plan_and_all_occurrence_ids_survive_fresh_runtime_with_duplicate_names()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();
        var dir=new ZipEntryPlan(1,null,"folder/","folder/",0,0,0,false,true,null,null);
        p=await f.Journal.AdmitPlanAsync(p.Fence,Plan(File(),dir,File(2,1)));
        var restored=await f.Journal.ReadAsync(p.Binding.OperationId);
        Assert.Equal(JsonSerializer.Serialize(p),JsonSerializer.Serialize(restored));
        Assert.Equal(2,restored!.Entries.Count(e=>e.Plan.FullName=="same.txt"));
        Assert.Null(restored.Entries[1].Plan.ChildOperationId);
        Assert.NotEqual(restored.Entries[0].Plan.ChildOperationId,restored.Entries[2].Plan.ChildOperationId);
        var replay=await f.Journal.AdmitPlanAsync(restored.Fence,restored.Plan!);
        Assert.Equal(restored.Fence,replay.Fence);
        Assert.Equal(3,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionEntries"));
    }
    [Fact]
    public async Task Stale_revision_epoch_owner_plan_and_frontier_reject_without_mutation()
    {
        await using var f=await Fixture.CreateAsync();var first=await f.ParentAsync();
        var p=await f.Journal.AdmitPlanAsync(first.Fence,Plan(File()));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(first.Fence,"StaleRevision"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(p.Fence with{PlanHash=new string('C',64)},"StalePlan"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(p.Fence with{ConfirmedOrdinal=0},"StaleFrontier"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ClaimAsync(p.Binding.OperationId,"worker-2",TimeSpan.FromMinutes(5)));
        f.Clock.Now+=TimeSpan.FromMinutes(6);
        var next=await f.Journal.ClaimAsync(p.Binding.OperationId,"worker-2",TimeSpan.FromMinutes(5));
        Assert.Equal(p.Fence.Epoch+1,next.Fence.Epoch);
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(p.Fence,"StaleOwner"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(next.Fence with{Owner="worker-1"},"OwnerOnly"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(next.Fence with{Epoch=p.Fence.Epoch},"EpochOnly"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewAsync(next.Fence with{Revision=p.Fence.Revision},"RevisionOnly"));
        Assert.Equal(next.Fence,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Fence);
    }
    [Fact]
    public async Task Invalid_plan_is_atomic_and_cannot_replace_persisted_plan()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AdmitPlanAsync(p.Fence,Plan(File(),File(1,1) with{ChildOperationId="child-0"})));
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionEntries"));
        p=await f.Journal.AdmitPlanAsync(p.Fence,Plan(File()));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AdmitPlanAsync(p.Fence,Plan(File(name:"changed"))));
        Assert.Equal("same.txt",(await f.Journal.ReadAsync(p.Binding.OperationId))!.Entries[0].Plan.FullName);
    }
    [Fact]
    public async Task Reservations_are_nonrefundable_idempotent_and_restart_cannot_reset_attempts()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();p=await f.Journal.AdmitPlanAsync(p.Fence,Plan(File()));
        var r=new ZipWorkReservation("attempt-1",ZipWorkKind.Extraction,0,10,5);
        p=await f.Journal.ReserveAsync(p.Fence,r);
        var replay=await f.Journal.ReserveAsync(p.Fence,r);Assert.Equal(p.Budget,replay.Budget);
        f.Clock.Now+=TimeSpan.FromMinutes(6);p=await f.Journal.ClaimAsync(p.Binding.OperationId,"worker-2",TimeSpan.FromMinutes(5));
        p=await f.Journal.ReserveAsync(p.Fence,r with{Id="attempt-2"});
        Assert.Equal(20,p.Budget.ExpandedReserved);Assert.Equal(10,p.Budget.ReplayExpanded);Assert.Equal(2,p.Budget.ProbeBytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReserveAsync(p.Fence,r with{Id="attempt-3"}));
        Assert.Equal(p.Budget,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Budget);
    }
    [Fact]
    public async Task Only_next_file_may_reserve_work_and_invalid_attempt_does_not_charge()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();p=await f.Journal.AdmitPlanAsync(p.Fence,Plan(File(),File(1,1)));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReserveAsync(p.Fence,new("skip",ZipWorkKind.Extraction,1,10,5)));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReserveAsync(p.Fence,new("wrong-length",ZipWorkKind.Extraction,0,11,5)));
        Assert.Equal(p.Budget,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Budget);
    }
    [Theory]
    [InlineData("UPDATE ZipExtractionEntries SET PlanJson='{}'")]
    [InlineData("DELETE FROM ZipExtractionEntries")]
    [InlineData("INSERT OR REPLACE INTO ZipExtractionEntries SELECT * FROM ZipExtractionEntries")]
    [InlineData("UPDATE ZipExtractionParents SET BindingJson='{}'")]
    [InlineData("UPDATE ZipExtractionParents SET OperationId='other'")]
    [InlineData("UPDATE ZipExtractionEntries SET ParentOperationId='other'")]
    [InlineData("INSERT OR REPLACE INTO ZipExtractionParents SELECT * FROM ZipExtractionParents")]
    public async Task Immutable_evidence_cannot_be_updated_deleted_or_replaced(string sql)
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();await f.Journal.AdmitPlanAsync(p.Fence,Plan(File()));
        await Assert.ThrowsAsync<SqliteException>(()=>f.SqlAsync(sql));
    }
    [Fact]
    public async Task Altered_schema_is_rejected_before_journal_operations()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();
        await f.SqlAsync("DROP TRIGGER ZipEntryBindingImmutable");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync(p.Binding.OperationId));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.InitializeAsync());
    }
    [Fact]
    public async Task Parent_identity_cannot_be_rebound_and_cancellation_cannot_create_partial_plan()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.CreateAsync(f.Binding with{ParentArtifactId="other"}));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Journal.AdmitPlanAsync(p.Fence,Plan(File()),cancelled.Token));
        Assert.Null((await f.Journal.ReadAsync(p.Binding.OperationId))!.Plan);
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionEntries"));
    }

    [Fact]
    public async Task Identity_collision_mid_plan_rolls_back_all_entries_and_parent_promotion()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();await f.Journal.AdmitPlanAsync(p.Fence,Plan(File()));
        var binding=f.Binding with{OperationId="second-parent",ParentArtifactId="second-artifact"};
        await f.Journal.CreateAsync(binding);var other=await f.Journal.ClaimAsync(binding.OperationId,"worker",TimeSpan.FromMinutes(5));
        other=await f.Journal.ReserveAsync(other.Fence,new("preflight",ZipWorkKind.Preflight,null,binding.Input.Length+65539));
        var first=File() with{ChildOperationId="new-child",ProvisionalArtifactId="new-artifact"};
        var collision=File(1,1) with{ChildOperationId="child-0",ProvisionalArtifactId="artifact-0"};
        await Assert.ThrowsAsync<SqliteException>(()=>f.Journal.AdmitPlanAsync(other.Fence,Plan(first,collision)));
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionEntries WHERE ParentOperationId='second-parent'"));
        Assert.Null((await f.Journal.ReadAsync(binding.OperationId))!.Plan);
    }

    [Theory]
    [InlineData("UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$.ExpandedReserved',-1)")]
    [InlineData("UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$.ExpandedReserved',209715201)")]
    [InlineData("UPDATE ZipExtractionParents SET State=999")]
    [InlineData("UPDATE ZipExtractionParents SET ConfirmedOrdinal=0")]
    public async Task Corrupted_budget_state_and_frontier_fail_closed_on_read(string sql)
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();await f.SqlAsync(sql);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync(p.Binding.OperationId));
    }

    [Fact]
    public async Task Reduced_counter_cannot_refund_append_only_reservations()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();p=await f.Journal.AdmitPlanAsync(p.Fence,Plan(File()));
        p=await f.Journal.ReserveAsync(p.Fence,new("spent",ZipWorkKind.Extraction,0,10,5));
        await f.SqlAsync("UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$.ExpandedReserved',0)");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync(p.Binding.OperationId));
    }

    [Fact]
    public async Task Checked_revision_and_epoch_overflow_leave_parent_unchanged()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();
        await f.SqlAsync("UPDATE ZipExtractionParents SET Epoch=9223372036854775807");
        await Assert.ThrowsAsync<OverflowException>(()=>f.Journal.ClaimAsync(p.Binding.OperationId,"worker-1",TimeSpan.FromMinutes(5)));
        Assert.Equal(long.MaxValue,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Fence.Epoch);
        await f.SqlAsync("UPDATE ZipExtractionParents SET Epoch=1,Revision=9223372036854775807");
        await Assert.ThrowsAsync<OverflowException>(()=>f.Journal.ClaimAsync(p.Binding.OperationId,"worker-1",TimeSpan.FromMinutes(5)));
        Assert.Equal(long.MaxValue,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Fence.Revision);
    }

    [Fact]
    public async Task Shared_ingestion_schema_and_existing_rows_are_unchanged_by_zip_migration()
    {
        await using var f=await EMF.Tests.TestInfrastructure.ArtifactIngestionFixture.CreateAsync();
        var outcome=await f.Restart().IngestAsync(f.Draft,f.Content);Assert.True(outcome.IsAdopted);
        async Task<string> Snapshot()
        {
            await using var c=new SqliteConnection("Data Source="+f.DatabasePath);await c.OpenAsync();
            using var q=c.CreateCommand();q.CommandText="SELECT name,sql FROM sqlite_master WHERE name NOT LIKE 'Zip%' AND tbl_name NOT LIKE 'Zip%' ORDER BY name";
            var schema=new List<string>();var tables=new List<string>();
            using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())schema.Add(r.GetString(0)+":"+(r.IsDBNull(1)?"":r.GetString(1)));
            q.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'Zip%' ORDER BY name";
            using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())tables.Add(r.GetString(0));
            foreach(var table in tables)
            {
                q.CommandText="SELECT * FROM \""+table.Replace("\"","\"\"")+"\" ORDER BY rowid";
                using var r=await q.ExecuteReaderAsync();
                while(await r.ReadAsync())
                {
                    var values=new object[r.FieldCount];r.GetValues(values);schema.Add(table+":"+JsonSerializer.Serialize(values));
                }
            }
            return JsonSerializer.Serialize(schema);
        }
        var before=await Snapshot();var journal=new SqliteZipExtractionJournal(f.DatabasePath);
        await journal.InitializeAsync();await journal.InitializeAsync();Assert.Equal(before,await Snapshot());
        // Existing ingestion admission still accepts the unchanged schema.
        await f.Persistence.InitializeAsync();Assert.Equal(before,await Snapshot());
    }

    [Fact]
    public async Task Checked_plan_aggregation_rejects_overflow_without_persisting_any_ids()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync();
        var plan=Plan(File() with{CompressedLength=long.MaxValue},File(1,1));
        await Assert.ThrowsAsync<OverflowException>(()=>f.Journal.AdmitPlanAsync(p.Fence,plan));
        Assert.Null((await f.Journal.ReadAsync(p.Binding.OperationId))!.Plan);
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionEntries"));
    }

    [Fact]
    public async Task Unsupported_zip_version_is_rejected_without_touching_evidence_tables()
    {
        await using var f=await Fixture.CreateAsync();await f.SqlAsync("INSERT INTO ZipExtractionSchema VALUES(6)");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.InitializeAsync());
        Assert.Equal(6,await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(3,await f.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Artifacts','Relationships','Provenance')"));
    }

    [Fact]
    public async Task Preflight_reservation_survives_restart_and_third_attempt_is_forbidden()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync(false);
        p=await f.Journal.ReserveAsync(p.Fence,new("preflight-1",ZipWorkKind.Preflight,null,f.Binding.Input.Length+65539));
        f.Clock.Now+=TimeSpan.FromMinutes(6);p=await f.Journal.ClaimAsync(p.Binding.OperationId,"worker-2",TimeSpan.FromMinutes(5));
        p=await f.Journal.ReserveAsync(p.Fence,new("preflight-2",ZipWorkKind.Preflight,null,f.Binding.Input.Length+65539));
        Assert.Equal(2,p.Budget.PreflightAttempts);Assert.Equal(2*(f.Binding.Input.Length+65539),p.Budget.PreflightBytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReserveAsync(p.Fence,new("preflight-3",ZipWorkKind.Preflight,null,f.Binding.Input.Length+65539)));
        Assert.Equal(p.Budget,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Budget);
    }

    [Fact]
    public async Task Preflight_work_can_be_reconciled_after_plan_with_multiple_directories()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync(false);
        p=await f.Journal.ReserveAsync(p.Fence,new("preflight",ZipWorkKind.Preflight,null,f.Binding.Input.Length+65539));
        var a=new ZipEntryPlan(0,null,"a/","a/",0,0,0,false,true,null,null);
        var b=a with{CentralOrdinal=1,FullName="b/",DisplayName="b/"};
        p=await f.Journal.AdmitPlanAsync(p.Fence,Plan(a,b,File(2,0)));
        Assert.Equal(p.Fence,(await f.Journal.ReadAsync(p.Binding.OperationId))!.Fence);
        f.Clock.Now+=TimeSpan.FromMinutes(6);
        var resumed=await f.Journal.ClaimAsync(p.Binding.OperationId,"worker-2",TimeSpan.FromMinutes(5));
        Assert.Equal(p.Budget,resumed.Budget);Assert.Equal(3,resumed.Entries.Count);
    }

    [Fact]
    public async Task Plan_admission_requires_a_durable_preflight_reservation()
    {
        await using var f=await Fixture.CreateAsync();var p=await f.ParentAsync(false);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AdmitPlanAsync(p.Fence,Plan(File())));
        Assert.Null((await f.Journal.ReadAsync(p.Binding.OperationId))!.Plan);
        Assert.Equal(0,await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionEntries"));
    }
}
