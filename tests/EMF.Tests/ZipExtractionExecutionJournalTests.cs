using System.Security.Cryptography;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Orchestration.Services;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;
public sealed class ZipExtractionExecutionJournalTests
{
    private sealed class Fixture:IAsyncDisposable
    {
        public ArtifactIngestionFixture Evidence=null!;public SqliteZipExtractionJournal Journal=>new(Evidence.DatabasePath);public ZipParentSnapshot Parent=null!;
        public static async Task<Fixture> Create(bool initialize=true)
        {
            var f=new Fixture{Evidence=await ArtifactIngestionFixture.CreateAsync()};if(initialize)await f.Journal.InitializeAsync();return f;
        }
        public async Task Plan(params long[] lengths)
        {
            var binding=new ZipParentBinding("op","parent",new("input","revision",new string('A',64),ZipNumericLimits.Parent),new string('B',64));await Journal.CreateAsync(binding);
            Parent=await Journal.ClaimAsync("op","owner",TimeSpan.FromMinutes(5));Parent=await Journal.ReserveAsync(Parent.Fence,new("preflight",ZipWorkKind.Preflight,null,ZipNumericLimits.Parent+65539));
            var entries=lengths.Select((n,i)=>new ZipEntryPlan(i,i,"child"+i,"child"+i,ZipNumericLimits.Parent/Math.Max(1,lengths.Length),n,0,false,false,"operation"+i,"artifact"+i)).ToArray();
            Parent=await Journal.AdmitPlanAsync(Parent.Fence,new(ZipPlanBinding.Compute("{}",entries),"{}",entries));
        }
        public async Task Sql(string sql)
        {await using var c=new SqliteConnection("Data Source="+Evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText=sql;await q.ExecuteNonQueryAsync();}
        public async Task<long> Scalar(string sql)
        {await using var c=new SqliteConnection("Data Source="+Evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(await q.ExecuteScalarAsync());}
        public async Task<ZipWorkReservation> Reserve(int ordinal,string id)
        {var e=Parent.Entries.Single(e=>e.Plan.FileOrdinal==ordinal).Plan;var r=new ZipWorkReservation(id,ZipWorkKind.Extraction,ordinal,e.ExpandedLength,e.CompressedLength);Parent=await Journal.ReserveAsync(Parent.Fence,r);return r;}
        public ValueTask DisposeAsync()=>Evidence.DisposeAsync();
    }
    [Fact]
    public async Task Exact_aggregate_expanded_replay_compressed_crc_and_parent_read_boundaries_hold()
    {
        await using var f=await Fixture.Create();await f.Plan(ZipNumericLimits.Child,ZipNumericLimits.Child);
        for(var ordinal=0;ordinal<2;ordinal++)
        {
            if(ordinal==1)
            {
                // This is a numeric journal boundary fixture, not a ZIP/CRC test.
                // Its admitted CRC is explicitly trusted here; frontier progression
                // still requires real retained content, scan, generic ingestion and ACK.
                var plaintext=new byte[checked((int)ZipNumericLimits.Child)];Array.Fill(plaintext,(byte)0x4E);
                try
                {
                    f.Parent=await f.Journal.VerifyChildAsync(f.Parent.Fence,new("attempt-0-1",0,plaintext.Length,0,Convert.ToHexString(SHA256.HashData(plaintext)),f.Parent.Plan!.Hash));
                    var storage=new ZipPrivateContentStorage(Path.Combine(f.Evidence.Root,"zip-private"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
                    var retention=new ZipProtectedRetentionService(f.Journal,f.Journal,storage,new DevelopmentEnvelopeEncryptionService(new ArtifactIngestionFixture.Keys()));
                    var created=await retention.SealAsync(f.Parent,0,plaintext);f.Parent=await f.Journal.MaterializedAsync(created.Parent.Fence,0,created.Retention);
                    f.Parent=await ZipAcknowledgementFixture.Ingest(f.Evidence,f.Journal,f.Parent,retention);
                    f.Parent=(await f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,0,f.Parent.Entries[0].Ingestion!)).Parent;
                }
                finally{CryptographicOperations.ZeroMemory(plaintext);}
            }
            for(var attempt=0;attempt<2;attempt++){var r=await f.Reserve(ordinal,$"attempt-{ordinal}-{attempt}");f.Parent=await f.Journal.ChargeAsync(f.Parent.Fence,new($"charge-{ordinal}-{attempt}",r.Id,ordinal,ZipNumericLimits.Child,ordinal==0&&attempt==0?ZipNumericLimits.ParentReads:0,ZipNumericLimits.Child));}
        }
        Assert.Equal(ZipNumericLimits.ExpandedWork,f.Parent.Budget.ExpandedReserved);Assert.Equal(ZipNumericLimits.ExpandedWork,f.Parent.Budget.ExpandedProduced);
        Assert.Equal(ZipNumericLimits.ExpandedWork,f.Parent.Budget.CrcBytes);Assert.Equal(ZipNumericLimits.Aggregate,f.Parent.Budget.ReplayExpanded);
        Assert.Equal(ZipNumericLimits.CompressedWork,f.Parent.Budget.CompressedReserved);Assert.Equal(ZipNumericLimits.ParentReads,f.Parent.Budget.ParentReadBytes);Assert.Equal(4,f.Parent.Budget.ProbeBytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ChargeAsync(f.Parent.Fence,new("read-overrun","attempt-1-1",1,0,1,0)));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ChargeAsync(f.Parent.Fence,new("crc-overrun","attempt-1-1",1,1,0,1)));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Reserve(1,"third"));Assert.Equal(f.Parent.Budget,(await f.Journal.ReadAsync("op"))!.Budget);
    }
    [Fact]
    public async Task Aggregate_one_byte_over_rejects_plan_atomically()
    {
        await using var f=await Fixture.Create();await Assert.ThrowsAsync<InvalidDataException>(()=>f.Plan(ZipNumericLimits.Child,ZipNumericLimits.Child,1));
        var p=(await f.Journal.ReadAsync("op"))!;Assert.Null(p.Plan);Assert.Empty(p.Entries);
    }
    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")] [InlineData("ordinal")]
    public async Task Charges_reject_every_independent_stale_fence_and_later_child(string part)
    {
        await using var f=await Fixture.Create();await f.Plan(10,10);var r=await f.Reserve(0,"attempt");var fence=f.Parent.Fence;
        fence=part switch{"owner"=>fence with{Owner="other"},"epoch"=>fence with{Epoch=fence.Epoch-1},"revision"=>fence with{Revision=fence.Revision-1},_=>fence with{ConfirmedOrdinal=0}};
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ChargeAsync(fence,new("charge",r.Id,0,10,0,10)));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ChargeAsync(f.Parent.Fence,new("later",r.Id,1,10,0,10)));Assert.Equal(0,f.Parent.Budget.ExpandedProduced);
    }
    [Fact]
    public async Task Charge_replay_is_idempotent_but_changed_identity_and_overflow_are_rejected()
    {
        await using var f=await Fixture.Create();await f.Plan(10);var r=await f.Reserve(0,"attempt");var charge=new ZipExtractionCharge("charge",r.Id,0,10,100,10);
        f.Parent=await f.Journal.ChargeAsync(f.Parent.Fence,charge);var replay=await f.Journal.ChargeAsync(f.Parent.Fence,charge);Assert.Equal(f.Parent.Fence,replay.Fence);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ChargeAsync(f.Parent.Fence,charge with{ParentReadBytes=101}));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ChargeAsync(f.Parent.Fence,new("huge",r.Id,0,long.MaxValue,0,long.MaxValue)));
        Assert.Equal(10,(await f.Journal.ReadAsync("op"))!.Budget.ExpandedProduced);
    }
    [Theory]
    [InlineData("ExpandedProduced")] [InlineData("CrcBytes")] [InlineData("ParentReadBytes")]
    public async Task Charged_counters_cannot_be_reduced_independently_of_immutable_ledger(string field)
    {
        await using var f=await Fixture.Create();await f.Plan(10);var r=await f.Reserve(0,"attempt");f.Parent=await f.Journal.ChargeAsync(f.Parent.Fence,new("charge",r.Id,0,10,100,10));
        foreach(var sql in new[]{"DELETE FROM ZipExtractionCharges","UPDATE ZipExtractionCharges SET ChargeJson='{}'","INSERT OR REPLACE INTO ZipExtractionCharges SELECT * FROM ZipExtractionCharges"})await Assert.ThrowsAsync<SqliteException>(()=>f.Sql(sql));
        await f.Sql("UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$."+field+"',0)");await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("op"));
    }
    [Fact]
    public async Task Integrity_proof_requires_complete_charged_work_and_exact_plan_crc_binding()
    {
        await using var f=await Fixture.Create();await f.Plan(10);var r=await f.Reserve(0,"attempt");var proof=new ZipVerifiedChild(r.Id,0,10,0,new string('C',64),f.Parent.Plan!.Hash);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.VerifyChildAsync(f.Parent.Fence,proof));
        f.Parent=await f.Journal.ChargeAsync(f.Parent.Fence,new("charge",r.Id,0,10,0,10));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.VerifyChildAsync(f.Parent.Fence,proof with{Crc32=1}));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.VerifyChildAsync(f.Parent.Fence,proof with{PlanHash="other"}));
        f.Parent=await f.Journal.VerifyChildAsync(f.Parent.Fence,proof);
        await Assert.ThrowsAsync<SqliteException>(()=>f.Sql("DELETE FROM ZipExtractionVerifications"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.VerifyChildAsync(f.Parent.Fence,proof with{Sha256=new string('D',64)}));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Version_two_upgrade_failure_rolls_back_and_fresh_restart_preserves_retention(bool late)
    {
        await using var f=await Fixture.Create();await f.Plan(f.Evidence.Content.Length);
        var storage=new ZipPrivateContentStorage(Path.Combine(f.Evidence.Root,"zip-private"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
        var service=new ZipProtectedRetentionService(f.Journal,f.Journal,storage,new DevelopmentEnvelopeEncryptionService(new ArtifactIngestionFixture.Keys()));
        var created=await service.SealAsync(f.Parent,0,f.Evidence.Content);f.Parent=created.Parent;
        // Remove only the additive, empty V3 objects to retain actual populated V2 journal/retention state.
        await f.Sql("DROP TABLE ZipParentAdmissionEvents; DROP TABLE ZipParentAdmissions; DROP TABLE ZipExtractionScans; DROP TABLE ZipExtractionCharges; DROP TABLE ZipExtractionVerifications; DELETE FROM ZipExtractionSchema WHERE Version>=3;");
        Assert.Equal(2,await f.Scalar("SELECT MAX(Version) FROM ZipExtractionSchema"));
        var collision=late?"CREATE TRIGGER ZipVerificationNoDelete BEFORE DELETE ON ZipExtractionRetentions BEGIN SELECT 1; END;":"CREATE TABLE ZipExtractionCharges(Collision INTEGER);";
        await f.Sql(collision);await Assert.ThrowsAsync<SqliteException>(()=>f.Journal.InitializeAsync());Assert.Equal(2,await f.Scalar("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(late?0:1,await f.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='ZipExtractionCharges'"));Assert.Equal(0,await f.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='ZipExtractionVerifications'"));
        await f.Sql(late?"DROP TRIGGER ZipVerificationNoDelete":"DROP TABLE ZipExtractionCharges");await f.Journal.InitializeAsync();Assert.Equal(5,await f.Scalar("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(created.Retention,await f.Journal.ReadRetentionAsync("op",0));Assert.Equal(created.Parent.Fence,(await f.Journal.ReadAsync("op"))!.Fence);
        Assert.Equal(0,await f.Scalar("SELECT COUNT(*) FROM ZipExtractionCharges"));
    }
    [Theory]
    [InlineData("reservation")] [InlineData("crc")] [InlineData("sha")] [InlineData("plan")] [InlineData("ordinal")]
    public async Task Restart_rejects_orphan_or_contradictory_integrity_rows(string part)
    {
        await using var f=await Fixture.Create();await f.Plan(10);var r=await f.Reserve(0,"attempt");f.Parent=await f.Journal.ChargeAsync(f.Parent.Fence,new("charge",r.Id,0,10,0,10));
        var proof=new ZipVerifiedChild(r.Id,0,10,0,new string('C',64),f.Parent.Plan!.Hash);
        proof=part switch{"reservation"=>proof with{ReservationId="orphan"},"crc"=>proof with{Crc32=1},"sha"=>proof with{Sha256=new string('Z',64)},"plan"=>proof with{PlanHash="changed"},_=>proof with{FileOrdinal=1}};
        var json=System.Text.Json.JsonSerializer.Serialize(proof);
        await f.Sql("INSERT INTO ZipExtractionVerifications VALUES('op',0,'"+json+"')");await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("op"));
    }

    [Theory]
    [InlineData("lower")] [InlineData("malformed")] [InlineData(null)]
    public async Task Invalid_fingerprint_cannot_commit_a_proof_that_poisons_restart(string? input)
    {
        await using var f=await Fixture.Create();await f.Plan(10);var r=await f.Reserve(0,"attempt");f.Parent=await f.Journal.ChargeAsync(f.Parent.Fence,new("charge",r.Id,0,10,0,10));
        var hash=input is null?null:input=="lower"?new string('a',64):new string('Z',64);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.VerifyChildAsync(f.Parent.Fence,new(r.Id,0,10,0,hash!,f.Parent.Plan!.Hash)));
        Assert.Equal(f.Parent.Fence,(await f.Journal.ReadAsync("op"))!.Fence);Assert.Equal(0,await f.Scalar("SELECT COUNT(*) FROM ZipExtractionVerifications"));
    }

}
