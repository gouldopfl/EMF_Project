using System.Security.Cryptography;
using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;
public sealed class ZipDurableExtractionRecoveryTests
{
    private sealed class Crash:Exception;
    [Fact]
    public async Task Production_driver_composes_real_stages_then_completes_and_releases_only_private_copies()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var parent=await f.CreateParent();
        var copy=(await f.Journal.ReadParentRetentionAsync("zip"))!;Assert.NotEqual(copy.Identity.Source.ContentId,copy.Identity.ObjectId);
        var result=await f.Driver.RunAsync(parent);Assert.Equal(ZipParentState.Released,result.State);Assert.Equal(0,result.Fence.ConfirmedOrdinal);
        Assert.Equal(1,f.Count("parent-encrypt"));Assert.Equal(1,f.Count("child-encrypt"));Assert.Equal(1,f.Count("scan"));Assert.Equal(1,f.Count("extraction-start"));Assert.Equal(1,f.Count("Ingesting"));
        var released=(await f.Journal.ReadParentRetentionAsync("zip"))!;Assert.Equal(ZipRetentionState.Released,released.State);Assert.Equal(copy.CreateReceipt!.CurrentRevision,released.ReleaseReceipt!.PriorRevision);
        Assert.Null(await f.ParentStorage.ReadProtectedAsync(new(copy.Identity.ObjectId),copy.CreateReceipt.CurrentRevision!.Value));
        var before=result.Fence;var decrypt=f.Count("parent-decrypt");Assert.Equal(before,(await f.Driver.RunAsync(result)).Fence);Assert.Equal(decrypt,f.Count("parent-decrypt"));
        Assert.Equal(before,(await f.Journal.CompleteParentAsync(before)).Fence);Assert.Equal(1,f.Count("scan"));
    }
    [Theory]
    [InlineData("PreparationStarted")] [InlineData("Staged")] [InlineData("CandidateBound")] [InlineData("PhysicalCreated")] [InlineData("Created")]
    public async Task Parent_creation_restart_never_reencrypts_an_existing_creation_identity(string boundary)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);await f.Journal.InitializeAsync();var bytes=ZipDurableRuntimeFixture.Zip();using var lease=new ArtifactContentReadLease(new("source-parent"),new("source-revision"),bytes.Length,bytes);
        f.ParentRetention.Checkpoint=n=>n==boundary?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>f.ParentRetention.RetainAsync("zip","parent",new string('B',64),lease));var prior=(await f.Journal.ReadParentRetentionAsync("zip"))!;var encryptions=f.Count("parent-encrypt");
        var fresh=new ZipDurableRuntimeFixture(evidence);
        if(boundary=="PreparationStarted")
        {await Assert.ThrowsAsync<InvalidDataException>(()=>fresh.ParentRetention.RetainAsync("zip","parent",new string('B',64),lease));Assert.Equal(ZipRetentionState.RequiresReview,(await fresh.Journal.ReadParentRetentionAsync("zip"))!.State);}
        else
        {var binding=await fresh.ParentRetention.RetainAsync("zip","parent",new string('B',64),lease);Assert.Equal(prior.Identity.ObjectId,binding.Input.ContentId);Assert.Equal(prior.Identity.CreateOperationId,(await fresh.Journal.ReadParentRetentionAsync("zip"))!.Identity.CreateOperationId);}
        Assert.Equal(encryptions,f.Count("parent-encrypt"));
    }
    [Fact]
    public async Task Created_parent_missing_physical_evidence_cannot_bind_a_new_journal_or_regenerate()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);await f.Journal.InitializeAsync();var bytes=ZipDurableRuntimeFixture.Zip();using var lease=new ArtifactContentReadLease(new("source-parent"),new("source-revision"),bytes.Length,bytes);
        f.ParentRetention.Checkpoint=n=>n=="Created"?throw new Crash():Task.CompletedTask;await Assert.ThrowsAsync<Crash>(()=>f.ParentRetention.RetainAsync("zip","parent",new string('B',64),lease));
        var r=(await f.Journal.ReadParentRetentionAsync("zip"))!;await using(var delete=await f.ParentStorage.PrepareDeleteAsync(new(r.Identity.ObjectId),r.CreateReceipt!.CurrentRevision!.Value,new(new("synthetic-delete"),new ArtifactContentOwnershipToken(r.Identity.OwnerToken))))Assert.Equal(ArtifactContentMutationOutcome.Deleted,(await delete.ExecuteAsync()).Outcome);
        var fresh=new ZipDurableRuntimeFixture(evidence);await Assert.ThrowsAsync<InvalidDataException>(()=>fresh.ParentRetention.RetainAsync("zip","parent",new string('B',64),lease));Assert.Null(await fresh.Journal.ReadAsync("zip"));Assert.Equal(1,f.Count("parent-encrypt"));Assert.Equal(ZipRetentionState.RequiresReview,(await fresh.Journal.ReadParentRetentionAsync("zip"))!.State);
    }
    [Fact]
    public async Task Completion_and_parent_release_require_all_confirmed_children_and_uncancelled_current_fence()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Journal.CompleteParentAsync(p.Fence));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.ParentRetention.ReleaseAsync(p));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.ParentRetention.ReleaseAsync(p,cancel.Token));
        var r=(await f.Journal.ReadParentRetentionAsync("zip"))!;Assert.Equal(ZipRetentionState.Created,r.State);Assert.Null(r.ReleaseReceipt);await using var raw=await f.ParentStorage.ReadProtectedAsync(new(r.Identity.ObjectId),r.CreateReceipt!.CurrentRevision!.Value);Assert.NotNull(raw);
    }
    [Theory]
    [InlineData("ReleasePending")] [InlineData("PhysicalReleased")] [InlineData("CandidateDeleted")] [InlineData("Released")]
    public async Task Parent_release_loss_recovers_from_exact_receipts_without_reopening_payload(string point)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();f.ParentRetention.Checkpoint=n=>n==point?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>f.Driver.RunAsync(p));var before=await f.Read();var decrypt=f.Count("parent-decrypt");var fresh=new ZipDurableRuntimeFixture(evidence);var result=await fresh.Driver.RunAsync(before);
        Assert.Equal(ZipParentState.Released,result.State);Assert.Equal(decrypt,f.Count("parent-decrypt"));Assert.Equal(1,f.Count("parent-encrypt"));Assert.Equal(1,f.Count("scan"));Assert.Equal(1,f.Count("Ingesting"));
    }
    private static async Task CorruptParentRecord(ArtifactIngestionFixture evidence,string mutation)
    {
        await using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();
        q.CommandText="SELECT name,sql FROM sqlite_master WHERE type='trigger' AND tbl_name='ZipExtractionRetentions'";var triggers=new List<(string Name,string Sql)>();
        using(var rows=await q.ExecuteReaderAsync())while(await rows.ReadAsync())triggers.Add((rows.GetString(0),rows.GetString(1)));
        using var tx=c.BeginTransaction();q.Transaction=tx;foreach(var trigger in triggers){q.CommandText="DROP TRIGGER "+trigger.Name;await q.ExecuteNonQueryAsync();}
        q.CommandText=mutation;await q.ExecuteNonQueryAsync();foreach(var trigger in triggers){q.CommandText=trigger.Sql;await q.ExecuteNonQueryAsync();}tx.Commit();
    }
    [Theory]
    [InlineData("missing")] [InlineData("source")] [InlineData("receipt")] [InlineData("malformed")] [InlineData("null-identity")] [InlineData("null-source")] [InlineData("null-hash")]
    public async Task Fresh_owner_can_only_mark_bad_parent_evidence_for_review_without_payload_work(string part)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();var encrypt=f.Count("parent-encrypt");var decrypt=f.Count("parent-decrypt");
        await CorruptParentRecord(evidence,part switch{"missing"=>"DELETE FROM ZipExtractionRetentions WHERE FileOrdinal=-1","source"=>"UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Identity.Source.Revision','foreign') WHERE FileOrdinal=-1","receipt"=>"UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.CreateReceipt.ArtifactId.Value','foreign') WHERE FileOrdinal=-1","null-identity"=>"UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Identity',NULL) WHERE FileOrdinal=-1","null-source"=>"UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Identity.Source',NULL) WHERE FileOrdinal=-1","null-hash"=>"UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Identity.Source.Sha256',NULL) WHERE FileOrdinal=-1",_=>"UPDATE ZipExtractionRetentions SET StateJson='{' WHERE FileOrdinal=-1"});
        var fresh=new ZipDurableRuntimeFixture(evidence);
        if(part=="missing")await Assert.ThrowsAsync<InvalidDataException>(()=>fresh.Driver.ResumeAsync("zip","worker",TimeSpan.FromMinutes(30)));
        else Assert.Equal(ZipParentState.RequiresReview,(await fresh.Driver.ResumeAsync("zip","worker",TimeSpan.FromMinutes(30))).State);
        Assert.Equal(ZipParentState.RequiresReview,(await f.Read()).State);Assert.Equal(encrypt,f.Count("parent-encrypt"));Assert.Equal(decrypt,f.Count("parent-decrypt"));Assert.Equal(0,f.Count("extraction-start"));var reviewed=await f.Read();Assert.Equal(reviewed.Fence,(await fresh.Driver.RunAsync(reviewed)).Fence);Assert.Equal(decrypt,f.Count("parent-decrypt"));
    }
    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")] [InlineData("plan")] [InlineData("frontier")]
    public async Task Review_only_reader_cannot_bypass_any_current_fence_component(string part)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();var fence=p.Fence;
        fence=part switch{"owner"=>fence with{Owner="stale"},"epoch"=>fence with{Epoch=fence.Epoch-1},"revision"=>fence with{Revision=fence.Revision-1},"plan"=>fence with{PlanHash="foreign"},_=>fence with{ConfirmedOrdinal=0}};
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReviewParentEvidenceAsync(fence,"ParentEvidenceFailure"));Assert.Equal(p.Fence,(await f.Read()).Fence);
    }
    [Fact]
    public async Task Review_only_reader_still_requires_unrelated_scan_and_ingestion_proof()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();f.Driver.Checkpoint=n=>n=="C"?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>f.Driver.RunAsync(p));p=await f.Read();await evidence.SqlAsync("UPDATE ZipExtractionEntries SET ProgressJson=json_set(ProgressJson,'$.EvidenceJson','{}')");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReviewParentEvidenceAsync(p.Fence,"ParentEvidenceFailure"));
        await using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT State FROM ZipExtractionParents WHERE OperationId='zip'";Assert.Equal((long)ZipParentState.Processing,Convert.ToInt64(await q.ExecuteScalarAsync()));
    }
    [Fact]
    public async Task Changed_parent_revision_after_completion_is_reviewed_without_deleting_replacement()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();f.Driver.Checkpoint=n=>n=="Completed"?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>f.Driver.RunAsync(p));p=await f.Read();var r=(await f.Journal.ReadParentRetentionAsync("zip"))!;
        var physical=new FileSystemArtifactContentStore(Path.Combine(evidence.Root,"zip-parent-private","zip-quarantine"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Parent));
        var changed=await physical.ReplaceIfRevisionMatchesAsync(new(r.Identity.ObjectId),r.CreateReceipt!.CurrentRevision!.Value,"replacement"u8.ToArray(),new(new("replacement-operation"),new ArtifactContentOwnershipToken(r.Identity.OwnerToken)));Assert.Equal(ArtifactContentMutationOutcome.Replaced,changed.Outcome);
        var fresh=new ZipDurableRuntimeFixture(evidence);await Assert.ThrowsAsync<InvalidDataException>(()=>fresh.Driver.RunAsync(p));Assert.Equal(ZipParentState.RequiresReview,(await f.Read()).State);
        Assert.Equal(changed.CurrentRevision,await physical.ReadCurrentRevisionAsync(new(r.Identity.ObjectId)));Assert.Null((await f.Journal.ReadParentRetentionAsync("zip"))!.ReleaseReceipt);
    }
    private sealed class ObservedStorage(IZipPrivateContentStorage inner):IZipPrivateContentStorage
    {
        internal Func<CancellationToken,Task>? BeforeRead;
        internal Func<ArtifactContentOperationId,ArtifactContentMutationReceipt?,ArtifactContentMutationReceipt?>? Receipt;
        public string NamespaceId=>inner.NamespaceId;public long MaximumProtectedBytes=>inner.MaximumProtectedBytes;
        public Task StageAsync(ArtifactContentOperationId op,ReadOnlyMemory<byte> bytes,CancellationToken ct=default)=>inner.StageAsync(op,bytes,ct);
        public Task<byte[]?> ReadCandidateAsync(ArtifactContentOperationId op,CancellationToken ct=default)=>inner.ReadCandidateAsync(op,ct);
        public async Task<ArtifactContentMutationReceipt?> GetReceiptAsync(ArtifactContentOperationId op,CancellationToken ct=default){var r=await inner.GetReceiptAsync(op,ct);return Receipt is null?r:Receipt(op,r);}
        public Task<IPreparedArtifactContentMutation> PrepareCreateAsync(EMF.Core.Models.Identities.ArtifactId id,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.PrepareCreateAsync(id,bytes,context,ct);
        public Task<IPreparedArtifactContentMutation> PrepareDeleteAsync(EMF.Core.Models.Identities.ArtifactId id,ArtifactContentRevision revision,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.PrepareDeleteAsync(id,revision,context,ct);
        public async Task<IArtifactContentReadLease?> ReadProtectedAsync(EMF.Core.Models.Identities.ArtifactId id,ArtifactContentRevision revision,CancellationToken ct=default){if(BeforeRead is not null)await BeforeRead(ct);return await inner.ReadProtectedAsync(id,revision,ct);}
        public Task<IZipPreparedCandidateCleanup> PrepareCandidateCleanupAsync(ArtifactContentOperationId op,string hash,CancellationToken ct=default)=>inner.PrepareCandidateCleanupAsync(op,hash,ct);
    }
    [Fact]
    public async Task Multiple_children_share_one_parent_lease_and_read_is_charged_before_payload_allocation()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent(true);var decrypt=f.Count("parent-decrypt");var reads=0;
        var observed=new ObservedStorage(f.ParentStorage){BeforeRead=async ct=>{reads++;var durable=await f.Read();Assert.Equal(p.Binding.Input.Length,durable.Budget.ParentReadBytes);Assert.Equal(decrypt,f.Count("parent-decrypt"));}};
        f.ParentRetention=new(f.Journal,f.Journal,observed,f.ParentCrypto);f.ComposeDriver();byte[]? first=null;
        f.Driver.Checkpoint=n=>{if(n=="B"){first??=f.ParentCrypto.LastPlaintext;Assert.Same(first,f.ParentCrypto.LastPlaintext);}return Task.CompletedTask;};
        var final=await f.Driver.RunAsync(p);Assert.Equal(ZipParentState.Released,final.State);Assert.Equal(1,reads);Assert.Equal(decrypt+1,f.Count("parent-decrypt"));Assert.NotNull(first);Assert.All(first,b=>Assert.Equal((byte)0,b));
    }
    [Fact]
    public async Task Repeated_runtime_reopens_accumulate_nonrefundable_durable_parent_work()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();
        for(var i=1;i<=3;i++)
        {
            var fresh=new ZipDurableRuntimeFixture(evidence);p=await fresh.Journal.ClaimAsync("zip","worker",TimeSpan.FromMinutes(30));var before=f.Count("parent-decrypt");
            var observed=new ObservedStorage(fresh.ParentStorage){BeforeRead=async ct=>{Assert.Equal(i*p.Binding.Input.Length,(await fresh.Read()).Budget.ParentReadBytes);Assert.Equal(before,f.Count("parent-decrypt"));}};
            var service=new ZipParentRetentionService(fresh.Journal,fresh.Journal,observed,fresh.ParentCrypto);var opened=await service.OpenAsync(p);p=opened.Parent;await opened.Lease.DisposeAsync();Assert.Equal(before+1,f.Count("parent-decrypt"));
        }
        Assert.Equal(3*p.Binding.Input.Length,(await f.Read()).Budget.ParentReadBytes);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_or_cancelled_reopen_remains_charged_before_payload_allocation(bool cancelled)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();var before=f.Count("parent-decrypt");using var cancel=new CancellationTokenSource();
        var observed=new ObservedStorage(f.ParentStorage){BeforeRead=async ct=>{Assert.Equal(p.Binding.Input.Length,(await f.Read()).Budget.ParentReadBytes);if(cancelled){cancel.Cancel();ct.ThrowIfCancellationRequested();}throw new IOException("synthetic read failure before allocation");}};
        var service=new ZipParentRetentionService(f.Journal,f.Journal,observed,f.ParentCrypto);
        if(cancelled)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.OpenAsync(p,cancel.Token));else await Assert.ThrowsAsync<IOException>(()=>service.OpenAsync(p));
        var fresh=new ZipDurableRuntimeFixture(evidence);Assert.Equal(p.Binding.Input.Length,(await fresh.Read()).Budget.ParentReadBytes);Assert.Equal(before,f.Count("parent-decrypt"));
    }
    [Fact]
    public async Task Cancellation_after_parent_decryption_prevents_handoff_clears_plaintext_and_keeps_charge()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();using var cancel=new CancellationTokenSource();var before=f.Count("parent-decrypt");
        f.ParentRetention.Checkpoint=n=>{if(n=="Opened"){Assert.Contains(f.ParentCrypto.LastPlaintext!,b=>b!=0);cancel.Cancel();}return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.ParentRetention.OpenAsync(p,cancel.Token));Assert.Equal(before+1,f.Count("parent-decrypt"));Assert.All(f.ParentCrypto.LastPlaintext!,b=>Assert.Equal((byte)0,b));Assert.Equal(p.Binding.Input.Length,(await f.Read()).Budget.ParentReadBytes);
    }
    [Theory]
    [InlineData("success")] [InlineData("exception")] [InlineData("cancel")]
    public async Task Parent_plaintext_is_cleared_before_process_gate_is_released(string mode)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();Task<IDisposable>? next=null;ReadOnlyMemory<byte> memory=default;using var cancel=new CancellationTokenSource();
        f.Driver.Checkpoint=n=>{if(n=="B"){memory=f.ParentCrypto.LastPlaintext!;Assert.Contains(memory.ToArray(),b=>b!=0);next=ZipParentReadAdmission.ProcessWide.AcquireAsync();Assert.False(next.IsCompleted);if(mode=="exception")throw new Crash();if(mode=="cancel")cancel.Cancel();}return Task.CompletedTask;};
        if(mode=="exception")await Assert.ThrowsAsync<Crash>(()=>f.Driver.RunAsync(p,cancel.Token));else if(mode=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Driver.RunAsync(p,cancel.Token));else await f.Driver.RunAsync(p,cancel.Token);
        Assert.NotNull(next);using var acquired=await next.WaitAsync(TimeSpan.FromSeconds(5));Assert.All(memory.ToArray(),b=>Assert.Equal((byte)0,b));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_or_contradictory_release_receipt_cannot_authorize_recovery_deletion(bool contradiction)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();f.ParentRetention.Checkpoint=n=>n=="CandidateDeleted"?throw new Crash():Task.CompletedTask;await Assert.ThrowsAsync<Crash>(()=>f.Driver.RunAsync(p));p=await f.Read();var r=(await f.Journal.ReadParentRetentionAsync("zip"))!;Assert.NotNull(r.ReleaseReceipt);var budget=p.Budget;var before=f.Count("parent-decrypt");
        var fresh=new ZipDurableRuntimeFixture(evidence);var observed=new ObservedStorage(fresh.ParentStorage){Receipt=(op,receipt)=>op.Value==r.Identity.ReleaseOperationId?(contradiction?receipt! with{PriorRevision=new("different-revision")}:null):receipt};
        fresh.ParentRetention=new(fresh.Journal,fresh.Journal,observed,fresh.ParentCrypto);fresh.ComposeDriver();await Assert.ThrowsAsync<InvalidDataException>(()=>fresh.Driver.RunAsync(p));
        var reviewed=await fresh.Read();Assert.Equal(ZipParentState.RequiresReview,reviewed.State);Assert.Equal(budget,reviewed.Budget);Assert.Equal(before,f.Count("parent-decrypt"));Assert.Equal(r,(await fresh.Journal.ReadParentRetentionAsync("zip"))!);
    }
    [Fact]
    public async Task Parent_read_work_ceiling_is_enforced_before_an_additional_reopen_charge()
    {
        // Pure journal counter fixture: no protected payload or decryption claim.
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var journal=new SqliteZipExtractionJournal(evidence.DatabasePath);await journal.InitializeAsync();
        var r=await journal.ReserveParentRetentionAsync("zip","parent",new("source","revision",new string('A',64),ZipNumericLimits.Parent),new string('B',64),new string('C',64));
        r=await journal.BeginParentPreparationAsync(r);r=await journal.BindParentCandidateAsync(r,new string('D',64));
        var receipt=new ArtifactContentMutationReceipt(new(r.Identity.CreateOperationId),new(r.Identity.ObjectId),ArtifactContentMutationKind.Create,null,new("retained-revision"),new(r.Identity.OwnerToken),ArtifactContentMutationOutcome.Created,DateTimeOffset.UtcNow,null);
        r=await journal.PromoteParentRetentionAsync(r,null,receipt);await journal.CreateAsync(new("zip","parent",new(r.Identity.ObjectId,"retained-revision",r.Identity.Source.Sha256,r.Identity.Source.Length),r.Identity.ProfileHash));var p=await journal.ClaimAsync("zip","worker",TimeSpan.FromMinutes(30));
        for(var i=0;i<4;i++)p=await journal.ChargeParentReadAsync(p.Fence);Assert.Equal(ZipNumericLimits.ParentReads,p.Budget.ParentReadBytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>journal.ChargeParentReadAsync(p.Fence));var current=(await journal.ReadAsync("zip"))!;Assert.Equal(p.Fence,current.Fence);Assert.Equal(p.Budget,current.Budget);
    }
    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")]
    public async Task Stale_parent_release_cannot_delete_completed_parent_copy(string part)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);var p=await f.CreateParent();f.Driver.Checkpoint=n=>n=="Completed"?throw new Crash():Task.CompletedTask;await Assert.ThrowsAsync<Crash>(()=>f.Driver.RunAsync(p));p=await f.Read();var r=(await f.Journal.ReadParentRetentionAsync("zip"))!;
        var fence=part switch{"owner"=>p.Fence with{Owner="stale"},"epoch"=>p.Fence with{Epoch=p.Fence.Epoch-1},_=>p.Fence with{Revision=p.Fence.Revision-1}};
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.ParentRetention.ReleaseAsync(p with{Fence=fence}));Assert.Equal(r,(await f.Journal.ReadParentRetentionAsync("zip"))!);await using var raw=await f.ParentStorage.ReadProtectedAsync(new(r.Identity.ObjectId),r.CreateReceipt!.CurrentRevision!.Value);Assert.NotNull(raw);
    }
    [Fact]
    public async Task Parent_profile_cannot_inherit_child_ceiling()
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);
        Assert.Throws<InvalidOperationException>(()=>new ZipParentRetentionService(f.Journal,f.Journal,f.ChildStorage,f.ParentCrypto));
    }
}
