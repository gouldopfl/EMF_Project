using System.Text.Json;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;
public sealed class ZipScanJournalTests
{
    private static MalwareScanEvidence Evidence(ZipScanAttempt a,bool clean=true)=>new(a.Request,
        clean?MalwareDetection.NoThreatDetected:MalwareDetection.Error,clean?MalwareCoverage.Complete:MalwareCoverage.Unknown,
        clean?MalwareScanFailure.None:MalwareScanFailure.TransportError,a.Request.Content.Length,a.Request.Content.Length,true,
        a.Request.Content.Sha256,"fixture-engine","fixture-db",DateTimeOffset.UtcNow,clean?"fixture-complete":null);
    private static MalwareScannerPolicy Policy()=>new("fixture","1",new string('C',64));
    [Fact]
    public async Task Atomic_binding_is_required_and_scan_reservation_cannot_use_generic_api()
    {
        await using var f=await ZipChildScanTests.Fixture.Create();
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReserveAsync(f.Parent.Fence,new("orphan",ZipWorkKind.Scanner,0,f.Parent.Entries[0].Plan.ExpandedLength)));
        Assert.Equal(0,(await f.Journal.ReadAsync("zip"))!.Budget.ScannerAttempts);
        var a=await f.Journal.BeginScanAsync(f.Parent.Fence,0,Policy());Assert.Equal(1,a.Parent.Budget.ScannerAttempts);
        var result=await f.Journal.CompleteScanAsync(a.Parent.Fence,a.Attempt,Evidence(a.Attempt));Assert.Equal(ZipEntryState.Scanned,result.Entries[0].State);
        var replay=await f.Journal.CompleteScanAsync(result.Fence,a.Attempt,EvidenceStored(result));Assert.Equal(result.Fence,replay.Fence);Assert.Equal(result.Budget,replay.Budget);
    }
    [Fact]
    public async Task Scan_binding_insert_failure_rolls_back_reservation_budget_and_revision()
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var before=f.Parent;
        await f.Sql("CREATE TRIGGER InjectScanBindingFailure BEFORE INSERT ON ZipExtractionScans BEGIN SELECT RAISE(ABORT,'injected binding failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(()=>f.Journal.BeginScanAsync(before.Fence,0,Policy()));
        await f.Sql("DROP TRIGGER InjectScanBindingFailure");
        var restarted=new SqliteZipExtractionJournal(f.Evidence.DatabasePath);await restarted.InitializeAsync();var restored=(await restarted.ReadAsync("zip"))!;
        Assert.Equal(before.Fence,restored.Fence);Assert.Equal(before.Budget,restored.Budget);
        await using(var c=new SqliteConnection("Data Source="+f.Evidence.DatabasePath)){await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE Kind=2";Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync()));q.CommandText="SELECT COUNT(*) FROM ZipExtractionScans";Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync()));}
        var retry=await restarted.BeginScanAsync(restored.Fence,0,Policy());Assert.Equal(1,retry.Parent.Budget.ScannerAttempts);Assert.Equal(Policy(),retry.Attempt.Request.Policy);
    }
    private static MalwareScanEvidence EvidenceStored(ZipParentSnapshot p)=>JsonSerializer.Deserialize<MalwareScanEvidence>(p.Entries[0].EvidenceJson!)!;
    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")] [InlineData("frontier")]
    public async Task Each_fence_component_independently_rejects_scan_begin_and_completion(string field)
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var fence=f.Parent.Fence;
        ZipFence Change(ZipFence value)=>field switch{"owner"=>value with{Owner="stale"},"epoch"=>value with{Epoch=value.Epoch-1},"revision"=>value with{Revision=value.Revision-1},_=>value with{ConfirmedOrdinal=0}};
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.BeginScanAsync(Change(fence),0,Policy()));
        var started=await f.Journal.BeginScanAsync(fence,0,Policy());await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.CompleteScanAsync(Change(started.Parent.Fence),started.Attempt,Evidence(started.Attempt)));
        Assert.Equal(ZipEntryState.Materialized,(await f.Journal.ReadAsync("zip"))!.Entries[0].State);
    }
    [Fact]
    public async Task Earlier_pending_attempt_cannot_complete_after_a_new_attempt_was_bound()
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var first=await f.Journal.BeginScanAsync(f.Parent.Fence,0,Policy());var second=await f.Journal.BeginScanAsync(first.Parent.Fence,0,Policy());
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.CompleteScanAsync(second.Parent.Fence,first.Attempt,Evidence(first.Attempt)));var result=await f.Journal.CompleteScanAsync(second.Parent.Fence,second.Attempt,Evidence(second.Attempt));Assert.Equal(2,result.Budget.ScannerAttempts);
    }
    [Fact]
    public async Task Third_pending_submission_rejects_without_refunding_first_two()
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var first=await f.Journal.BeginScanAsync(f.Parent.Fence,0,Policy());var second=await f.Journal.BeginScanAsync(first.Parent.Fence,0,Policy());
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.BeginScanAsync(second.Parent.Fence,0,Policy()));var p=(await f.Journal.ReadAsync("zip"))!;Assert.Equal(2,p.Budget.ScannerAttempts);Assert.Equal(2*f.Parent.Entries[0].Plan.ExpandedLength,p.Budget.ScannerReserved);
    }
    [Theory]
    [InlineData("length")] [InlineData("hash")] [InlineData("revision")] [InlineData("policy")]
    public async Task Evidence_cannot_change_the_bound_child_or_policy(string field)
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var a=await f.Journal.BeginScanAsync(f.Parent.Fence,0,Policy());var e=Evidence(a.Attempt);var r=e.Request;
        r=field switch{"length"=>r with{Content=r.Content with{Length=r.Content.Length+1}},"hash"=>r with{Content=r.Content with{Sha256=new string('D',64)}},"revision"=>r with{Content=r.Content with{Revision="changed"}},_=>r with{Policy=r.Policy with{Hash=new string('D',64)}}};
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.CompleteScanAsync(a.Parent.Fence,a.Attempt,e with{Request=r}));Assert.Equal(ZipEntryState.Materialized,(await f.Journal.ReadAsync("zip"))!.Entries[0].State);
    }
    [Fact]
    public async Task Restart_rejects_orphan_scanner_reservation_even_if_counter_snapshot_was_adjusted()
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var r=new ZipWorkReservation("orphan",ZipWorkKind.Scanner,0,f.Parent.Entries[0].Plan.ExpandedLength);
        await f.Sql("INSERT INTO ZipExtractionReservations VALUES('zip','orphan',0,2,'"+JsonSerializer.Serialize(r)+"');UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$.ScannerAttempts',1,'$.ScannerReserved',"+r.ExpandedBytes+")");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Historical_clean_cannot_authorize_ingestion_after_latest_nonclean_or_pending_attempt(bool pending)
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var first=await f.Journal.BeginScanAsync(f.Parent.Fence,0,Policy());
        var result=await f.Journal.CompleteScanAsync(first.Parent.Fence,first.Attempt,Evidence(first.Attempt));
        // Corruption fixture bypasses normal transitions; both serialized bindings remain syntactically valid.
        var next=first.Attempt with{ReservationId="later"};var r=new ZipWorkReservation(next.ReservationId,ZipWorkKind.Scanner,0,next.Request.Content.Length);var e=Evidence(next,false);
        var progress=result.Entries[0] with{EvidenceJson=pending?result.Entries[0].EvidenceJson:JsonSerializer.Serialize(e)};
        var evidenceSql=pending?"NULL":"'"+JsonSerializer.Serialize(e)+"'";
        await f.Sql("INSERT INTO ZipExtractionReservations VALUES('zip','later',0,2,'"+JsonSerializer.Serialize(r)+"'); INSERT INTO ZipExtractionScans VALUES('zip','later',0,'"+JsonSerializer.Serialize(next)+"',"+evidenceSql+");UPDATE ZipExtractionEntries SET ProgressJson='"+JsonSerializer.Serialize(progress)+"'; UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$.ScannerAttempts',2,'$.ScannerReserved',"+(2*r.ExpandedBytes)+",'$.ReplayScanner',"+r.ExpandedBytes+")");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));
    }
    [Theory]
    [InlineData("ScannerReserved")] [InlineData("ReplayScanner")] [InlineData("ScannerAttempts")]
    public async Task Restart_cannot_reduce_any_scanner_counter(string field)
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var a=await f.Journal.BeginScanAsync(f.Parent.Fence,0,Policy());var b=await f.Journal.BeginScanAsync(a.Parent.Fence,0,Policy());
        await f.Sql("UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$."+field+"',0)");await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Version_three_migration_collision_rolls_back_and_restart_preserves_materialization(bool late)
    {
        await using var f=await ZipChildScanTests.Fixture.Create();var parent=f.Parent;var retained=(await f.Journal.ReadRetentionAsync("zip",0))!;
        await f.Sql("DROP TABLE ZipExtractionScans;DELETE FROM ZipExtractionSchema WHERE Version=4;");
        await f.Sql(late?"CREATE TRIGGER ZipScanNoReplace BEFORE DELETE ON ZipExtractionRetentions BEGIN SELECT 1; END;":"CREATE TABLE ZipExtractionScans(Collision INTEGER);");
        await Assert.ThrowsAsync<SqliteException>(()=>f.Journal.InitializeAsync());
        await using(var c=new SqliteConnection("Data Source="+f.Evidence.DatabasePath)){await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT MAX(Version) FROM ZipExtractionSchema";Assert.Equal(3,Convert.ToInt32(await q.ExecuteScalarAsync()));q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='ZipScanBindingImmutable'";Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync()));}
        await f.Sql(late?"DROP TRIGGER ZipScanNoReplace":"DROP TABLE ZipExtractionScans");await new SqliteZipExtractionJournal(f.Evidence.DatabasePath).InitializeAsync();
        var restored=(await f.Journal.ReadAsync("zip"))!;Assert.Equal(parent.Fence,restored.Fence);Assert.Equal(parent.Budget,restored.Budget);Assert.Equal(retained,await f.Journal.ReadRetentionAsync("zip",0));Assert.Equal(ZipEntryState.Materialized,restored.Entries[0].State);
    }
}
