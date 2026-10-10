using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;
public sealed class ZipAcknowledgementTests
{
    private sealed class Crash:Exception;
    private sealed class Clock:TimeProvider
    {internal DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Fixture:IAsyncDisposable
    {
        internal ZipChildScanTests.Fixture Slice=null!;
        internal ZipParentSnapshot Parent{get=>Slice.Parent;set=>Slice.Parent=value;}
        internal SqliteZipExtractionJournal Journal=>Slice.Journal;
        internal ZipChildIngestionProof Proof=>Parent.Entries.Single(e=>e.Plan.FileOrdinal==Parent.Fence.ConfirmedOrdinal+1).Ingestion!;
        internal static async Task<Fixture> Create(bool multiple=false)
        {
            ZipChildScanTests.Fixture slice;
            if(!multiple)slice=await ZipChildScanTests.Fixture.Create();
            else
            {
                using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
                {zip.CreateEntry("directory/");for(var i=0;i<2;i++){using var entry=zip.CreateEntry("duplicate.txt").Open();entry.Write("identical child payload"u8);}}
                var array=bytes.ToArray();slice=new(){Evidence=await ArtifactIngestionFixture.CreateAsync()};await slice.Journal.InitializeAsync();
                await slice.Journal.CreateAsync(new("zip","parent",new("input","revision",Convert.ToHexString(SHA256.HashData(array)),array.Length),new string('B',64)));
                slice.Parent=await slice.Journal.ClaimAsync("zip","owner",TimeSpan.FromMinutes(5));slice.Lease=new(new("input"),new("revision"),array.Length,array);
                slice.Parent=await new ZipEntryPlanner().AdmitAsync(slice.Journal,slice.Parent,slice.Lease);
                slice.Parent=await new ZipSequentialExtractor(slice.Journal,slice.Journal,slice.Journal,slice.Retention()).MaterializeNextAsync(slice.Parent,slice.Lease);
            }
            var f=new Fixture{Slice=slice};f.Parent=await ZipAcknowledgementFixture.Ingest(slice.Evidence,slice.Journal,slice.Parent,slice.Retention());return f;
        }
        internal Task<ZipAcknowledgementUpdate> Ack(SqliteZipExtractionJournal? journal=null,CancellationToken ct=default)=>
            (journal??Journal).AcknowledgeEntryAsync(Parent.Fence,Parent.Fence.ConfirmedOrdinal+1,Proof,ct);
        internal async Task<long> Count(string table)
        {await using var c=new SqliteConnection("Data Source="+Slice.Evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM "+table;return Convert.ToInt64(await q.ExecuteScalarAsync());}
        internal Task Sql(string sql)=>Slice.Sql(sql);
        internal Task Refresh()=>Slice.Refresh();
        public ValueTask DisposeAsync()=>Slice.DisposeAsync();
    }
    [Fact]
    public async Task Atomic_ack_records_exact_occurrence_two_directions_frontier_and_revision_without_release()
    {
        await using var f=await Fixture.Create();var before=f.Parent;var proof=f.Proof;var result=await f.Ack();
        Assert.False(result.AlreadyAcknowledged);Assert.Equal(0,result.Parent.Fence.ConfirmedOrdinal);Assert.Equal(before.Fence.Revision+1,result.Parent.Fence.Revision);
        Assert.Equal(ZipEntryState.Acknowledged,result.Parent.Entries[0].State);Assert.Equal(2,await f.Count("Relationships"));Assert.Equal(1,await f.Count("ZipExtractionAcknowledgements"));
        Assert.Equal(proof,result.Acknowledgement.Details!.Ingestion);Assert.Equal(before.Fence,result.Acknowledgement.Details.AdmittedFence);
        Assert.Equal(result.Parent.Fence,result.Acknowledgement.Details.CommittedFence);Assert.Equal(before.Binding.Input,result.Acknowledgement.Details.ParentInput);
        Assert.Equal(before.Entries[0].Retained,result.Acknowledgement.Details.ChildInput);Assert.NotEqual(result.ContainsId,result.DerivedFromId);
        Assert.Equal(ZipRetentionState.Created,(await f.Journal.ReadRetentionAsync("zip",0))!.State);
        await using var lease=await f.Slice.Retention().OpenAsync(result.Parent,0);Assert.Equal(before.Entries[0].Plan.ExpandedLength,lease.Content.Length);
    }
    [Theory]
    [InlineData("Validated")] [InlineData("Contains")] [InlineData("DerivedFrom")] [InlineData("Acknowledgement")] [InlineData("Progress")] [InlineData("Frontier")]
    public async Task Every_precommit_exception_rolls_back_all_then_fresh_runtime_can_retry(string point)
    {
        await using var f=await Fixture.Create();var before=f.Parent;var journal=f.Journal;journal.AcknowledgementCheckpoint=n=>{if(n==point)throw new Crash();};
        await Assert.ThrowsAsync<Crash>(()=>f.Ack(journal));await f.Refresh();Assert.Equal(before.Fence,f.Parent.Fence);Assert.Equal(ZipEntryState.Ingested,f.Parent.Entries[0].State);
        Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(0,await f.Count("ZipExtractionAcknowledgements"));
        var result=await f.Ack();Assert.False(result.AlreadyAcknowledged);Assert.Equal(2,await f.Count("Relationships"));
    }
    [Theory]
    [InlineData("Validated")] [InlineData("Contains")] [InlineData("DerivedFrom")] [InlineData("Acknowledgement")] [InlineData("Progress")] [InlineData("Frontier")]
    public async Task Every_precommit_cancellation_rolls_back_without_authorizing_retention_delete(string point)
    {
        await using var f=await Fixture.Create();var before=f.Parent;using var cts=new CancellationTokenSource();var journal=f.Journal;
        journal.AcknowledgementCheckpoint=n=>{if(n==point)cts.Cancel();};await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Ack(journal,cts.Token));
        await f.Refresh();Assert.Equal(before.Fence,f.Parent.Fence);Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(0,await f.Count("ZipExtractionAcknowledgements"));
        Assert.Equal(ZipRetentionState.Created,(await f.Journal.ReadRetentionAsync("zip",0))!.State);
    }
    [Fact]
    public async Task Lost_commit_response_replays_from_fresh_runtime_with_same_owned_ids_and_no_revision_change()
    {
        await using var f=await Fixture.Create();var before=f.Parent;var proof=f.Proof;var journal=f.Journal;journal.AcknowledgementCheckpoint=n=>{if(n=="Committed")throw new Crash();};
        await Assert.ThrowsAsync<Crash>(()=>f.Ack(journal));await f.Refresh();var committed=f.Parent;
        var replay=await f.Journal.AcknowledgeEntryAsync(committed.Fence,0,proof);Assert.True(replay.AlreadyAcknowledged);Assert.Equal(committed.Fence,replay.Parent.Fence);
        var again=await f.Journal.AcknowledgeEntryAsync(committed.Fence,0,proof);Assert.Equal(replay.ContainsId,again.ContainsId);Assert.Equal(replay.DerivedFromId,again.DerivedFromId);Assert.Equal(replay.Acknowledgement,again.Acknowledgement);Assert.Equal(2,await f.Count("Relationships"));Assert.Equal(1,await f.Count("ZipExtractionAcknowledgements"));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.AcknowledgeEntryAsync(before.Fence,0,proof));
    }
    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")] [InlineData("plan")] [InlineData("frontier")]
    public async Task Each_stale_fence_component_rejects_before_relationships(string part)
    {
        await using var f=await Fixture.Create();var fence=f.Parent.Fence;fence=part switch{"owner"=>fence with{Owner="stale"},"epoch"=>fence with{Epoch=fence.Epoch-1},"revision"=>fence with{Revision=fence.Revision-1},"plan"=>fence with{PlanHash=new string('A',64)},_=>fence with{ConfirmedOrdinal=0}};
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.AcknowledgeEntryAsync(fence,0,f.Proof));Assert.Equal(0,await f.Count("Relationships"));
    }
    [Fact]
    public async Task Journal_revision_overflow_rolls_back_relationships_and_ack()
    {
        await using var f=await Fixture.Create();await f.Sql("UPDATE ZipExtractionParents SET Revision=9223372036854775807");await f.Refresh();
        await Assert.ThrowsAsync<OverflowException>(()=>f.Ack());Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(0,await f.Count("ZipExtractionAcknowledgements"));
        Assert.Equal(long.MaxValue,(await f.Journal.ReadAsync("zip"))!.Fence.Revision);
    }
    [Fact]
    public async Task Real_owner_takeover_blocks_old_worker_and_new_owner_can_ack()
    {
        await using var f=await Fixture.Create();var old=f.Parent;var proof=f.Proof;await f.Sql("UPDATE ZipExtractionParents SET OwnerUntil='2000-01-01T00:00:00.0000000+00:00'");
        f.Parent=await f.Journal.ClaimAsync("zip","new-owner",TimeSpan.FromMinutes(5));await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.AcknowledgeEntryAsync(old.Fence,0,proof));
        Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(0,(await f.Ack()).Parent.Fence.ConfirmedOrdinal);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Lease_expiry_during_transaction_or_replay_fails_closed(bool replay)
    {
        await using var f=await Fixture.Create();var proof=f.Proof;if(replay)f.Parent=(await f.Ack()).Parent;
        var clock=new Clock{Now=f.Parent.OwnerUntil.AddSeconds(-1)};var journal=new SqliteZipExtractionJournal(f.Slice.Evidence.DatabasePath,clock);
        journal.AcknowledgementCheckpoint=n=>{if(n==(replay?"Validated":"Frontier"))clock.Now=f.Parent.OwnerUntil;};
        await Assert.ThrowsAsync<ZipFenceException>(()=>journal.AcknowledgeEntryAsync(f.Parent.Fence,0,proof));
        Assert.Equal(replay?2:0,await f.Count("Relationships"));Assert.Equal(replay?1:0,await f.Count("ZipExtractionAcknowledgements"));await f.Refresh();Assert.Equal(replay?0:-1,f.Parent.Fence.ConfirmedOrdinal);
    }
    [Theory]
    [InlineData("canonical")] [InlineData("sha")] [InlineData("operation")] [InlineData("classification")]
    public async Task Caller_proof_must_equal_exact_durable_ingestion_proof(string part)
    {
        await using var f=await Fixture.Create();var proof=f.Proof;proof=part switch{"canonical"=>proof with{CanonicalArtifactId="foreign"},"sha"=>proof with{Sha256=new string('D',64)},"operation"=>proof with{ChildOperationId="foreign"},_=>proof with{ClassificationRevision="foreign"}};
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,0,proof));Assert.Equal(0,await f.Count("Relationships"));
    }
    [Theory]
    [InlineData("review")] [InlineData("receipt")] [InlineData("authority")] [InlineData("audit")]
    public async Task Generic_evidence_is_revalidated_even_when_zip_state_is_Ingested(string part)
    {
        await using var f=await Fixture.Create();var id=f.Proof.ChildOperationId;var proof=f.Proof;
        await f.Sql(part switch{"review"=>"INSERT INTO ArtifactIngestionReviews VALUES('"+id+"','Contradiction','fixture',NULL)","receipt"=>"UPDATE ArtifactIngestionIntents SET IntentJson=json_set(IntentJson,'$.CreateReceipt.ArtifactId.Value','foreign') WHERE OperationId='"+id+"'","authority"=>"UPDATE ArtifactMutationAuthority SET IsAdopted=0",_=>"DELETE FROM ArtifactIngestionAudit WHERE OperationId='"+id+"'"});
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,0,proof));Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(0,await f.Count("ZipExtractionAcknowledgements"));
    }
    [Fact]
    public async Task Ingested_state_without_typed_proof_cannot_authorize_ack()
    {
        await using var f=await Fixture.Create();var proof=f.Proof;await f.Sql("UPDATE ZipExtractionEntries SET ProgressJson=json_remove(ProgressJson,'$.Ingestion')");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,0,proof));Assert.Equal(0,await f.Count("Relationships"));
    }
    [Theory]
    [InlineData("delete")] [InlineData("type")] [InlineData("source")] [InlineData("target")] [InlineData("properties")] [InlineData("created")]
    public async Task Owned_relationship_corruption_rejects_read_and_replay(string part)
    {
        await using var f=await Fixture.Create();var proof=f.Proof;var result=await f.Ack();var id=result.ContainsId;
        await f.Sql(part switch{"delete"=>"DELETE FROM Relationships WHERE Id="+id,"type"=>"UPDATE Relationships SET RelationshipType='DerivedFrom' WHERE Id="+id,"source"=>"UPDATE Relationships SET SourceArtifactId='foreign' WHERE Id="+id,"target"=>"UPDATE Relationships SET TargetArtifactId='foreign' WHERE Id="+id,"properties"=>"UPDATE Relationships SET PropertiesJson='{}' WHERE Id="+id,_=>"UPDATE Relationships SET CreatedUtc='2000-01-01T00:00:00Z' WHERE Id="+id});
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(result.Parent.Fence,0,proof));
    }
    [Fact]
    public async Task Foreign_endpoint_and_copied_metadata_rows_are_not_reconciled_as_owned_occurrences()
    {
        await using var f=await Fixture.Create();var e=f.Parent.Entries[0];var occurrence=new ZipRelationshipOccurrence("zip","parent",f.Parent.Fence.PlanHash!,e.Plan.CentralOrdinal,0,e.Plan.ChildOperationId!,e.Plan.ProvisionalArtifactId!,f.Proof.CanonicalArtifactId,f.Proof.Sha256);
        await using(var c=new SqliteConnection("Data Source="+f.Slice.Evidence.DatabasePath))
        {await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="INSERT INTO Relationships(SourceArtifactId,TargetArtifactId,RelationshipType,CreatedUtc,PropertiesJson) VALUES('parent',$id,'Contains',$time,$metadata),($id,'parent','DerivedFrom',$time,$metadata)";q.Parameters.AddWithValue("$id",f.Proof.CanonicalArtifactId);q.Parameters.AddWithValue("$time",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$metadata",JsonSerializer.Serialize(occurrence));await q.ExecuteNonQueryAsync();}
        var result=await f.Ack();Assert.True(result.ContainsId>2);Assert.True(result.DerivedFromId>2);Assert.Equal(4,await f.Count("Relationships"));
        var replay=await f.Journal.AcknowledgeEntryAsync(result.Parent.Fence,0,f.Proof);Assert.Equal(result.ContainsId,replay.ContainsId);Assert.Equal(4,await f.Count("Relationships"));
    }
    [Fact]
    public async Task Protected_retention_release_is_separate_and_requires_committed_ack()
    {
        await using var f=await Fixture.Create();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Slice.Retention().ReleaseAsync(f.Parent,0));
        f.Parent=(await f.Ack()).Parent;Assert.Equal(ZipRetentionState.Created,(await f.Journal.ReadRetentionAsync("zip",0))!.State);
        var released=await f.Slice.Retention().ReleaseAsync(f.Parent,0);Assert.Equal(ZipRetentionState.Released,released.Retention.State);Assert.Equal(0,released.Parent.Fence.ConfirmedOrdinal);Assert.Equal(2,await f.Count("Relationships"));
        // Stage 4 records release on its private retention record; it does not
        // rewrite the immutable acknowledgement or advance the frontier again.
        Assert.Equal(ZipEntryState.Acknowledged,(await f.Journal.ReadAsync("zip"))!.Entries[0].State);
        var replay=await f.Journal.AcknowledgeEntryAsync(released.Parent.Fence,0,f.Parent.Entries[0].Ingestion!);Assert.True(replay.AlreadyAcknowledged);Assert.Equal(2,await f.Count("Relationships"));
    }
    [Fact]
    public async Task Independent_readers_observe_none_of_the_uncommitted_relationship_ack_or_frontier()
    {
        await using var f=await Fixture.Create();var before=f.Parent;var journal=f.Journal;var observed=new List<string>();
        journal.AcknowledgementCheckpoint=n=>
        {
            if(n=="Committed")return;
            using var c=new SqliteConnection("Data Source="+f.Slice.Evidence.DatabasePath);c.Open();using var q=c.CreateCommand();
            q.CommandText="SELECT (SELECT COUNT(*) FROM Relationships),(SELECT COUNT(*) FROM ZipExtractionAcknowledgements),ConfirmedOrdinal,Revision FROM ZipExtractionParents WHERE OperationId='zip'";
            using var row=q.ExecuteReader();Assert.True(row.Read());Assert.Equal(0,row.GetInt64(0));Assert.Equal(0,row.GetInt64(1));Assert.Equal(-1,row.GetInt32(2));Assert.Equal(before.Fence.Revision,row.GetInt64(3));observed.Add(n);
        };
        await f.Ack(journal);Assert.Equal(new[]{"Validated","Contains","DerivedFrom","Acknowledgement","Progress","Frontier"},observed);Assert.Equal(2,await f.Count("Relationships"));
    }
    [Theory]
    [InlineData("update")] [InlineData("delete")] [InlineData("replace")]
    public async Task Acknowledgement_receipt_cannot_be_updated_deleted_or_replaced(string mutation)
    {
        await using var f=await Fixture.Create();var result=await f.Ack();
        await Assert.ThrowsAsync<SqliteException>(()=>f.Sql(mutation switch{"update"=>"UPDATE ZipExtractionAcknowledgements SET ReceiptJson='{}'","delete"=>"DELETE FROM ZipExtractionAcknowledgements",_=>"INSERT OR REPLACE INTO ZipExtractionAcknowledgements SELECT * FROM ZipExtractionAcknowledgements"}));
        Assert.Equal(result.Parent.Fence,(await f.Journal.ReadAsync("zip"))!.Fence);Assert.Equal(2,await f.Count("Relationships"));
    }
    [Theory]
    [InlineData("missing")] [InlineData("plan")] [InlineData("fence")] [InlineData("ids")]
    public async Task Receipt_reconstruction_rejects_corruption_independently_of_immutable_triggers(string part)
    {
        await using var f=await Fixture.Create();var proof=f.Proof;var result=await f.Ack();
        // Deliberately bypass then restore the exact trigger SQL in this test DB,
        // so Read must detect the contradictory receipt itself, not missing schema.
        await using(var c=new SqliteConnection("Data Source="+f.Slice.Evidence.DatabasePath))
        {
            await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT name,sql FROM sqlite_master WHERE type='trigger' AND tbl_name='ZipExtractionAcknowledgements'";
            var triggers=new List<(string Name,string Sql)>();using(var rows=await q.ExecuteReaderAsync())while(await rows.ReadAsync())triggers.Add((rows.GetString(0),rows.GetString(1)));
            Assert.NotEmpty(triggers);using var tx=c.BeginTransaction();q.Transaction=tx;
            foreach(var trigger in triggers){q.CommandText="DROP TRIGGER "+trigger.Name;await q.ExecuteNonQueryAsync();}
            q.CommandText=part switch{"missing"=>"DELETE FROM ZipExtractionAcknowledgements","plan"=>"UPDATE ZipExtractionAcknowledgements SET ReceiptJson=json_set(ReceiptJson,'$.PlanHash','foreign')","fence"=>"UPDATE ZipExtractionAcknowledgements SET ReceiptJson=json_set(ReceiptJson,'$.Details.CommittedFence.Revision',0)",_=>"UPDATE ZipExtractionAcknowledgements SET DerivedFromId=ContainsId"};await q.ExecuteNonQueryAsync();
            foreach(var trigger in triggers){q.CommandText=trigger.Sql;await q.ExecuteNonQueryAsync();}tx.Commit();
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(result.Parent.Fence,0,proof));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_latest_bound_clean_rejects_initial_ack_and_acknowledged_replay(bool acknowledged)
    {
        await using var f=await Fixture.Create();var proof=f.Proof;if(acknowledged)f.Parent=(await f.Ack()).Parent;
        await f.Sql("UPDATE ZipExtractionEntries SET ProgressJson=json_set(ProgressJson,'$.EvidenceJson','{}')");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,0,proof));Assert.Equal(acknowledged?2:0,await f.Count("Relationships"));
    }
    [Fact]
    public async Task Next_occurrence_requires_previous_ack_and_duplicate_names_stay_distinct()
    {
        await using var f=await Fixture.Create(true);var first=f.Parent.Entries.Single(e=>e.Plan.FileOrdinal==0);await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,1,f.Proof));Assert.Equal(0,await f.Count("Relationships"));var firstResult=await f.Ack();f.Parent=firstResult.Parent;
        // Seed the second source provenance for the already-governed canonical
        // artifact. The second real coordinator submission now resolves to that
        // same canonical artifact while keeping its distinct stable operation.
        var secondPlan=f.Parent.Entries.Single(e=>e.Plan.FileOrdinal==1).Plan;
        await f.Slice.Evidence.Repository.AddProvenanceAsync(new(){ArtifactId=new(first.Ingestion!.CanonicalArtifactId),Source="zip-parent:parent/entry:"+secondPlan.CentralOrdinal,RecordedBy="canonical-dedup-fixture"});
        f.Parent=await new ZipSequentialExtractor(f.Journal,f.Journal,f.Journal,f.Slice.Retention()).MaterializeNextAsync(f.Parent,f.Slice.Lease);
        f.Parent=await ZipAcknowledgementFixture.Ingest(f.Slice.Evidence,f.Journal,f.Parent,f.Slice.Retention());var second=f.Parent.Entries.Single(e=>e.Plan.FileOrdinal==1);
        Assert.Equal(first.Ingestion!.CanonicalArtifactId,second.Ingestion!.CanonicalArtifactId);Assert.NotEqual(second.Plan.ProvisionalArtifactId,second.Ingestion.CanonicalArtifactId);Assert.Equal(first.Plan.FullName,second.Plan.FullName);Assert.NotEqual(first.Plan.ChildOperationId,second.Plan.ChildOperationId);Assert.NotEqual(first.Plan.ProvisionalArtifactId,second.Plan.ProvisionalArtifactId);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,2,f.Proof));var result=await f.Ack();Assert.Equal(1,result.Parent.Fence.ConfirmedOrdinal);
        Assert.Equal(4,await f.Count("Relationships"));Assert.Equal(2,await f.Count("ZipExtractionAcknowledgements"));Assert.NotEqual(firstResult.ContainsId,result.ContainsId);Assert.NotEqual(firstResult.DerivedFromId,result.DerivedFromId);
    }
}
