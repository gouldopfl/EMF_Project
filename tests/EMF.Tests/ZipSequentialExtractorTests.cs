using System.IO.Compression;
using EMF.Core.Contracts.Malware;
using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;
public sealed class ZipSequentialExtractorTests
{
    private sealed class Crash:Exception;
    private sealed class Fixture:IAsyncDisposable
    {
        public ArtifactIngestionFixture Evidence=null!;public byte[] Bytes=null!;public ArtifactContentReadLease Lease=null!;
        public SqliteZipExtractionJournal Journal=>new(Evidence.DatabasePath);
        public ZipParentSnapshot Parent=null!;
        public ZipPrivateContentStorage Storage=>new(Path.Combine(Evidence.Root,"private-zip"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
        public ZipSequentialExtractor Extractor(Func<string,Task>? retentionCheckpoint=null)
        {
            var service=new ZipProtectedRetentionService(Journal,Journal,Storage,new DevelopmentEnvelopeEncryptionService(new ArtifactIngestionFixture.Keys())){Checkpoint=retentionCheckpoint};
            return new(Journal,Journal,Journal,service);
        }
        public static async Task<Fixture> Create(byte[] bytes)
        {
            var f=new Fixture{Evidence=await ArtifactIngestionFixture.CreateAsync(),Bytes=bytes};
            try
            {
            await f.Journal.InitializeAsync();
            var binding=new ZipParentBinding("zip","parent",new("retained","revision",Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length),new string('B',64));
            await f.Journal.CreateAsync(binding);f.Parent=await f.Journal.ClaimAsync("zip","owner",TimeSpan.FromMinutes(5));
            f.Lease=new(new("retained"),new("revision"),bytes.Length,bytes);f.Parent=await new ZipEntryPlanner().AdmitAsync(f.Journal,f.Parent,f.Lease);return f;
            }
            catch{f.Lease?.Dispose();await f.Evidence.DisposeAsync();throw;}
        }
        public async Task Refresh()=>Parent=(await Journal.ReadAsync("zip"))!;
        public async Task Sql(string sql)
        {await using var c=new SqliteConnection("Data Source="+Evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText=sql;await q.ExecuteNonQueryAsync();}
        public async ValueTask DisposeAsync(){Lease.Dispose();await Evidence.DisposeAsync();}
    }
    private static byte[] Zip(params (string Name,byte[] Content)[] entries)
    {
        using var s=new MemoryStream();using(var zip=new ZipArchive(s,ZipArchiveMode.Create,true))foreach(var e in entries){using var stream=zip.CreateEntry(e.Name,CompressionLevel.Fastest).Open();stream.Write(e.Content);}
        return s.ToArray(); // Fixture construction, not a parent copy on the extraction path.
    }
    private static int Central(byte[] b)=>checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(b.Length-6)));
    private static void U32(byte[] b,int at,uint v)=>System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at),v);
    [Fact]
    public async Task One_child_is_retained_crc_verified_and_plaintext_cleared_without_publication()
    {
        await using var f=await Fixture.Create(Zip(("a.txt","123456789"u8.ToArray()),("b.txt","second"u8.ToArray())));byte[]? buffer=null;var extractor=f.Extractor();extractor.Allocated=b=>buffer=b;
        f.Parent=await extractor.MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[0].State);Assert.Equal(ZipEntryState.Planned,f.Parent.Entries[1].State);
        Assert.Equal(0xCBF43926u,f.Parent.Entries[0].Plan.Crc32);Assert.NotNull(buffer);Assert.True(buffer.All(b=>b==0));Assert.Equal(-1,f.Parent.Fence.ConfirmedOrdinal);
        var r=(await f.Journal.ReadRetentionAsync("zip",0))!;Assert.Equal(ZipRetentionState.Created,r.State);Assert.Null(await f.Journal.ReadRetentionAsync("zip",1));
        Assert.Null(await f.Evidence.Repository.GetArtifactAsync(new(r.Identity.ObjectId)));Assert.Null(await f.Evidence.Repository.GetArtifactAsync(new(f.Parent.Entries[0].Plan.ProvisionalArtifactId!)));
        var attempts=f.Parent.Budget.ExtractionAttempts;f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(attempts,f.Parent.Budget.ExtractionAttempts);
        Assert.Equal(9,f.Parent.Budget.ExpandedProduced);Assert.Equal(9,f.Parent.Budget.CrcBytes);Assert.Equal(1,f.Parent.Budget.ProbeBytes);Assert.True(f.Parent.Budget.ParentReadBytes>0);
    }
    [Theory]
    [InlineData("crc")] [InlineData("short")] [InlineData("overrun")]
    public async Task Corrupt_crc_or_declared_length_fails_before_retention(string kind)
    {
        var b=Zip(("a","123456789"u8.ToArray()));var c=Central(b);U32(b,c+(kind=="crc"?16:24),kind=="crc"?0u:kind=="short"?10u:8u);
        await using var f=await Fixture.Create(b);byte[]? captured=null;var x=f.Extractor();x.Allocated=v=>captured=v;
        await Assert.ThrowsAnyAsync<Exception>(()=>x.MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();
        Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(1,f.Parent.Budget.RejectedEntries);Assert.Null(await f.Journal.ReadRetentionAsync("zip",0));Assert.True(captured!.All(v=>v==0));
        Assert.Equal(1,f.Parent.Budget.ProbeBytes);Assert.Equal(kind=="short"?10:kind=="overrun"?8:9,f.Parent.Budget.ExpandedProduced);
    }
    [Fact]
    public async Task Encrypted_child_rejects_before_allocation_open_or_extraction_reservation()
    {
        var b=Zip(("a","payload"u8.ToArray()));var c=Central(b);b[c+8]|=1;b[6]|=1;
        await using var f=await Fixture.Create(b);var x=f.Extractor();var allocated=0;var opened=0;x.Allocated=_=>allocated++;x.Checkpoint=n=>{if(n=="Opened")opened++;return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidDataException>(()=>x.MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();Assert.Equal(0,allocated);Assert.Equal(0,opened);Assert.Equal(0,f.Parent.Budget.ExtractionAttempts);
    }
    [Theory]
    [InlineData("Reserved")] [InlineData("Allocated")] [InlineData("PayloadCharged")] [InlineData("Decompressed")] [InlineData("IntegrityPersisted")]
    public async Task Interrupted_extraction_restarts_with_nonrefundable_replay_charge(string boundary)
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));var x=f.Extractor();x.Checkpoint=n=>n==boundary?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>x.MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();var before=f.Parent.Budget;
        f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(2,f.Parent.Budget.ExtractionAttempts);Assert.Equal(200,f.Parent.Budget.ExpandedReserved);Assert.Equal(100,f.Parent.Budget.ReplayExpanded);
        Assert.Equal(2,f.Parent.Budget.ProbeBytes);Assert.True(f.Parent.Budget.ExpandedProduced>=before.ExpandedProduced);Assert.True(f.Parent.Budget.ParentReadBytes>=before.ParentReadBytes);
    }
    [Fact]
    public async Task Third_extraction_attempt_rejects_before_plaintext_allocation()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[10])));
        for(var i=0;i<2;i++){var x=f.Extractor();x.Checkpoint=n=>n=="Reserved"?throw new Crash():Task.CompletedTask;await Assert.ThrowsAsync<Crash>(()=>x.MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();}
        var allocations=0;var final=f.Extractor();final.Allocated=_=>allocations++;await Assert.ThrowsAsync<InvalidDataException>(()=>final.MaterializeNextAsync(f.Parent,f.Lease));Assert.Equal(0,allocations);await f.Refresh();Assert.Equal(2,f.Parent.Budget.ExtractionAttempts);
    }
    [Fact]
    public async Task Cancellation_after_charge_clears_plaintext_and_keeps_spent_work()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));using var ct=new CancellationTokenSource();byte[]? bytes=null;var x=f.Extractor();x.Allocated=b=>{bytes=b;b.AsSpan().Fill(0xA5);};x.Checkpoint=n=>{if(n=="PayloadCharged")ct.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>x.MaterializeNextAsync(f.Parent,f.Lease,ct.Token));Assert.True(bytes!.All(b=>b==0));await f.Refresh();Assert.Equal(100,f.Parent.Budget.ExpandedProduced);Assert.Equal(100,f.Parent.Budget.CrcBytes);
        f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(200,f.Parent.Budget.ExpandedProduced);Assert.Equal(2,f.Parent.Budget.ExtractionAttempts);
    }
    [Fact]
    public async Task Decompression_crc_and_retention_run_without_sqlite_write_transaction()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));var x=f.Extractor();var checks=0;
        x.Checkpoint=async n=>{if(n is "Decompressed" or "CrcVerified" or "Retained"){await f.Sql("BEGIN IMMEDIATE; ROLLBACK;");checks++;}};
        await x.MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(3,checks);
    }
    [Fact]
    public async Task Process_wide_gate_serializes_two_executor_instances_until_first_plaintext_clears()
    {
        await using var a=await Fixture.Create(Zip(("a",new byte[100])));await using var b=await Fixture.Create(Zip(("b",new byte[100])));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);byte[]? first=null;var secondStarted=false;
        var x=a.Extractor();x.Allocated=v=>first=v;x.Checkpoint=async n=>{if(n=="Allocated"){entered.SetResult();await release.Task;}};
        var y=b.Extractor();y.Allocated=_=>{Assert.True(first!.All(v=>v==0));secondStarted=true;};var t1=x.MaterializeNextAsync(a.Parent,a.Lease);await entered.Task;
        var t2=y.MaterializeNextAsync(b.Parent,b.Lease);Assert.False(secondStarted);release.SetResult();await Task.WhenAll(t1,t2);Assert.True(secondStarted);
    }
    [Fact]
    public async Task Fifty_MiB_child_boundary_is_enforced_before_and_during_materialization()
    {
        var source=new byte[checked((int)ZipNumericLimits.Child)];source.AsSpan().Fill((byte)'A');var b=Zip(("large.bin",source));
        await using(var f=await Fixture.Create(b)){var x=f.Extractor();long allocated=0;x.Allocated=v=>allocated=v.LongLength;var p=await x.MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(ZipNumericLimits.Child,allocated);Assert.Equal(ZipNumericLimits.Child,p.Budget.ExpandedProduced);Assert.Equal(ZipNumericLimits.Child,p.Budget.CrcBytes);}
        var oversized=Zip(("large.bin",source));U32(oversized,Central(oversized)+24,checked((uint)ZipNumericLimits.Child+1));
        await Assert.ThrowsAsync<InvalidDataException>(()=>Fixture.Create(oversized));
    }
    [Fact]
    public async Task Expanded_work_counter_cannot_be_lowered_or_ledger_deleted_after_restart()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);
        await Assert.ThrowsAsync<SqliteException>(()=>f.Sql("DELETE FROM ZipExtractionCharges"));
        await f.Sql("UPDATE ZipExtractionParents SET BudgetJson=json_set(BudgetJson,'$.ExpandedProduced',0)");await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));
    }
    [Theory]
    [InlineData("Staged")] [InlineData("CandidateBound")] [InlineData("PhysicalCreated")] [InlineData("Created")]
    public async Task Protected_retention_reconciliation_never_reextracts_or_receives_fresh_budget(string boundary)
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));var x=f.Extractor(n=>n==boundary?throw new Crash():Task.CompletedTask);
        await Assert.ThrowsAsync<Crash>(()=>x.MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();var before=f.Parent.Budget;var r=(await f.Journal.ReadRetentionAsync("zip",0))!;
        f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(before,f.Parent.Budget);Assert.Equal(1,f.Parent.Budget.ExtractionAttempts);
        Assert.Equal(r.Identity,(await f.Journal.ReadRetentionAsync("zip",0))!.Identity);Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[0].State);
    }
    [Fact]
    public async Task Interrupted_preparation_without_ciphertext_reviews_without_another_extraction_attempt()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));var x=f.Extractor(n=>n=="PreparationStarted"?throw new Crash():Task.CompletedTask);
        await Assert.ThrowsAsync<Crash>(()=>x.MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Extractor().MaterializeNextAsync(f.Parent,f.Lease));await f.Refresh();Assert.Equal(1,f.Parent.Budget.ExtractionAttempts);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
    }
    [Theory]
    [InlineData(2)] [InlineData(3)]
    public async Task Reentry_preserves_advanced_child_progress_and_original_work_budget(int state)
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);
        var scan=await f.Journal.BeginScanAsync(f.Parent.Fence,0,new("fixture","1",new string('C',64)));f.Parent=scan.Parent;
        var evidence=new MalwareScanEvidence(scan.Attempt.Request,MalwareDetection.NoThreatDetected,MalwareCoverage.Complete,MalwareScanFailure.None,100,100,true,
            scan.Attempt.Request.Content.Sha256,"fixture-engine","fixture-db",DateTimeOffset.UtcNow,"fixture-complete");
        f.Parent=await f.Journal.CompleteScanAsync(f.Parent.Fence,scan.Attempt,evidence);var before=f.Parent.Budget;
        if(state==3){await f.Sql("UPDATE ZipExtractionEntries SET ProgressJson=json_set(ProgressJson,'$.State',3); UPDATE ZipExtractionParents SET Revision=Revision+1;");await f.Refresh();}
        f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal((ZipEntryState)state,f.Parent.Entries[0].State);Assert.Equal(before,f.Parent.Budget);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReserveAsync(f.Parent.Fence,new("irrelevant",ZipWorkKind.Extraction,0,100,f.Parent.Entries[0].Plan.CompressedLength)));
    }
    [Fact]
    public async Task Next_child_follows_confirmed_frontier_even_with_duplicate_names()
    {
        await using var f=await Fixture.Create(Zip(("same","first"u8.ToArray()),("same","second"u8.ToArray())));f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);
        var retention=new ZipProtectedRetentionService(f.Journal,f.Journal,f.Storage,new DevelopmentEnvelopeEncryptionService(new ArtifactIngestionFixture.Keys()));
        f.Parent=await ZipAcknowledgementFixture.Ingest(f.Evidence,f.Journal,f.Parent,retention);
        f.Parent=(await f.Journal.AcknowledgeEntryAsync(f.Parent.Fence,0,f.Parent.Entries[0].Ingestion!)).Parent;
        f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Equal(ZipEntryState.Acknowledged,f.Parent.Entries[0].State);Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[1].State);
        Assert.Equal(2,f.Parent.Budget.ExtractionAttempts);Assert.NotNull(await f.Journal.ReadRetentionAsync("zip",1));Assert.Equal(0,f.Parent.Fence.ConfirmedOrdinal);
    }
    [Fact]
    public async Task Caller_modified_occurrence_with_valid_fence_cannot_replace_canonical_plan()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));var caller=f.Parent with{Entries=new[]{f.Parent.Entries[0] with{Plan=f.Parent.Entries[0].Plan with{IsEncrypted=true,ExpandedLength=1}}}};
        var result=await f.Extractor().MaterializeNextAsync(caller,f.Lease);Assert.Equal(100,result.Entries[0].Retained!.Length);Assert.False(result.Entries[0].Plan.IsEncrypted);
    }

    private sealed class UnknownStream(byte[] bytes):MemoryStream(bytes,false);
    private sealed class UnknownLease(ArtifactContentReadLease inner,byte[] bytes):IArtifactContentReadLease
    {
        public EMF.Core.Models.Identities.ArtifactId ArtifactId=>inner.ArtifactId;
        public ArtifactContentRevision Revision=>inner.Revision;public long StoredLength=>inner.StoredLength;public long ReturnedLength=>inner.ReturnedLength;
        public ReadOnlyMemory<byte> Content=>inner.Content;public Stream OpenReadStream()=>new UnknownStream(bytes);
        public void Dispose(){}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    [Fact]
    public async Task Unsupported_stream_is_rejected_without_adaptation_child_allocation_or_reservation()
    {
        await using var f=await Fixture.Create(Zip(("a",new byte[100])));var x=f.Extractor();var allocations=0;x.Allocated=_=>allocations++;
        await Assert.ThrowsAsync<InvalidDataException>(()=>x.MaterializeNextAsync(f.Parent,new UnknownLease(f.Lease,f.Bytes)));await f.Refresh();Assert.Equal(0,allocations);Assert.Equal(0,f.Parent.Budget.ExtractionAttempts);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
    }
    [Fact]
    public async Task Directories_are_skipped_and_empty_file_still_spends_one_probe()
    {
        await using var f=await Fixture.Create(Zip(("folder/",Array.Empty<byte>()),("folder/empty",Array.Empty<byte>())));
        f.Parent=await f.Extractor().MaterializeNextAsync(f.Parent,f.Lease);Assert.Null(f.Parent.Entries[0].Plan.FileOrdinal);Assert.Equal(ZipEntryState.Planned,f.Parent.Entries[0].State);
        Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[1].State);Assert.Equal(0,f.Parent.Budget.ExpandedProduced);Assert.Equal(0,f.Parent.Budget.CrcBytes);Assert.Equal(1,f.Parent.Budget.ProbeBytes);
    }

}
