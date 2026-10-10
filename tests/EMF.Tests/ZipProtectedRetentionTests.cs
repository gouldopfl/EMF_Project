using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ZipProtectedRetentionTests
{
    private sealed class Crash : Exception;
    private sealed class Encryption : IEnvelopeEncryptionService,IBoundedEnvelopeDecryptionService
    {
        private readonly DevelopmentEnvelopeEncryptionService _inner=new(new ArtifactIngestionFixture.Keys());
        public int Encryptions;
        public byte[]? Ciphertext;
        public byte[]? Plaintext;
        public Action? AfterDecrypt;
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> p,CancellationToken ct=default)=>_inner.EncryptAsync(p,ct);
        public Task<byte[]> DecryptAsync(EncryptedEnvelope e,CancellationToken ct=default)=>_inner.DecryptAsync(e,ct);
        public async Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> p,ReadOnlyMemory<byte> a,CancellationToken ct=default)
        { Encryptions++;var e=await _inner.EncryptWithContextAsync(p,a,ct);Ciphertext=e.Ciphertext;return e; }
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope e,ReadOnlyMemory<byte> a,CancellationToken ct=default)=>_inner.DecryptWithContextAsync(e,a,ct);
        public async Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope e,ReadOnlyMemory<byte> a,EnvelopeDecryptionLimits l,CancellationToken ct=default)
        { Plaintext=await _inner.DecryptWithContextBoundedAsync(e,a,l,ct);AfterDecrypt?.Invoke();return Plaintext; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public ArtifactIngestionFixture Ingestion=null!;
        public Clock Time=new();
        public SqliteZipExtractionJournal Journal=>new(Ingestion.DatabasePath,Time);
        public Encryption Encryption=new();
        public string Root=>Path.Combine(Ingestion.Root,"private-zip");
        public ZipPrivateContentStorage Storage=>new(Root,BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
        public ZipProtectedRetentionService Service()=>new(Journal,Journal,Storage,Encryption);
        public ZipParentSnapshot Parent=null!;
        public static async Task<Fixture> Create()
        {
            var f=new Fixture{Ingestion=await ArtifactIngestionFixture.CreateAsync()};var j=f.Journal;await j.InitializeAsync();
            var binding=new ZipParentBinding("parent-operation","parent-artifact",new("retained-parent","revision",new string('A',64),100),new string('B',64));
            await j.CreateAsync(binding);var p=await j.ClaimAsync(binding.OperationId,"owner",TimeSpan.FromMinutes(5));
            p=await j.ReserveAsync(p.Fence,new("preflight",ZipWorkKind.Preflight,null,65639));
            ZipEntryPlan[] entries=[new(0,0,"child.txt","child.txt",10,f.Ingestion.Content.Length,0,false,false,
                f.Ingestion.SecurityContext.Operation.OperationId.Value,f.Ingestion.Id.Value)];
            const string receipt="{}";f.Parent=await j.AdmitPlanAsync(p.Fence,new(ZipPlanBinding.Compute(receipt,entries),receipt,entries));return f;
        }
        public async Task Refresh()=>Parent=(await Journal.ReadAsync(Parent.Binding.OperationId))!;
        public async Task<ZipRetentionRecord> Retention()=> (await Journal.ReadRetentionAsync(Parent.Binding.OperationId,0))!;
        public async Task Sql(string sql,params (string,object)[] parameters)
        {
            await using var c=new SqliteConnection("Data Source="+Ingestion.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText=sql;
            foreach(var (name,value) in parameters)q.Parameters.AddWithValue(name,value);await q.ExecuteNonQueryAsync();
        }
        public string CandidatePath(ZipRetentionRecord r)=>Path.Combine(Root,"zip-candidates",Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(r.Identity.CreateOperationId)))+".candidate");
        // Stage8 now uses actual generic ingestion and the atomic production ACK.
        public async Task Acknowledge()
        {
            Parent=await ZipAcknowledgementFixture.MaterializationBoundary(Ingestion,Journal,Parent,Ingestion.Content);
            Parent=await ZipAcknowledgementFixture.Ingest(Ingestion,Journal,Parent,Service());
            Parent=(await Journal.AcknowledgeEntryAsync(Parent.Fence,0,Parent.Entries[0].Ingestion!)).Parent;
        }
        public ValueTask DisposeAsync()=>Ingestion.DisposeAsync();
    }

    [Fact]
    public async Task Private_retention_never_publishes_artifact_and_owned_read_clears_plaintext()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        Assert.Equal(ZipRetentionState.Created,created.Retention.State);Assert.Equal(1,f.Encryption.Encryptions);
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(new(created.Retention.Identity.ObjectId)));
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(f.Ingestion.Id));
        Assert.Null(await f.Ingestion.Physical.ReadAsync(f.Ingestion.Id));
        using var plain=await f.Service().OpenAsync(f.Parent,0);var escaped=plain.Content;
        Assert.Equal(f.Ingestion.Content,plain.Content.ToArray());plain.Dispose();Assert.True(escaped.Span.IndexOfAnyExcept((byte)0)<0);
        Assert.True(f.Encryption.Ciphertext!.All(b=>b==0));
    }
    [Theory]
    [InlineData("Reserved",true)] [InlineData("Staged",true)] [InlineData("CandidateBound",true)] [InlineData("PhysicalCreated",true)] [InlineData("Created",true)]
    [InlineData("PreparationStarted",false)]
    public async Task Fresh_runtime_recovers_create_boundaries_without_regenerating_randomized_candidate(string boundary,bool recover)
    {
        await using var f=await Fixture.Create();var service=f.Service();service.Checkpoint=n=>n==boundary?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.SealAsync(f.Parent,0,f.Ingestion.Content));await f.Refresh();var before=await f.Retention();
        var count=f.Encryption.Encryptions;
        if(recover)
        {
            var result=await f.Service().SealAsync(f.Parent,0,boundary=="Reserved"?(ReadOnlyMemory<byte>?)f.Ingestion.Content:null);
            Assert.Equal(before.Identity,result.Retention.Identity);Assert.Equal(ZipRetentionState.Created,result.Retention.State);
            Assert.Equal(boundary=="Reserved"?count+1:count,f.Encryption.Encryptions);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().SealAsync(f.Parent,0,f.Ingestion.Content));
            Assert.Equal(0,f.Encryption.Encryptions);Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);
            await f.Refresh();Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
        }
    }
    [Fact]
    public async Task Frozen_candidate_is_persisted_before_physical_promotion()
    {
        await using var f=await Fixture.Create();var service=f.Service();service.Checkpoint=n=>n=="CandidateBound"?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.SealAsync(f.Parent,0,f.Ingestion.Content));var r=await f.Retention();
        Assert.NotNull(r.CandidateHash);Assert.Null(await f.Storage.GetReceiptAsync(new(r.Identity.CreateOperationId)));
        var staged=await f.Storage.ReadCandidateAsync(new(r.Identity.CreateOperationId));Assert.Equal(r.CandidateHash,Convert.ToHexString(SHA256.HashData(staged!)));
    }
    [Fact]
    public async Task Changed_or_missing_bound_candidate_requires_review_without_reencryption()
    {
        foreach(var missing in new[]{true,false})
        {
            await using var f=await Fixture.Create();var service=f.Service();service.Checkpoint=n=>n=="CandidateBound"?throw new Crash():Task.CompletedTask;
            await Assert.ThrowsAsync<Crash>(()=>service.SealAsync(f.Parent,0,f.Ingestion.Content));await f.Refresh();var r=await f.Retention();
            var path=f.CandidatePath(r);if(missing)File.Delete(path);else await File.WriteAllBytesAsync(path,"invalid"u8.ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().SealAsync(f.Parent,0,f.Ingestion.Content));
            Assert.Equal(1,f.Encryption.Encryptions);Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);
        }
    }
    [Fact]
    public async Task Release_requires_acknowledgement_and_cancelled_token_grants_no_delete()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service().ReleaseAsync(f.Parent,0));
        await f.Acknowledge();using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Service().ReleaseAsync(f.Parent,0,cancelled.Token));
        Assert.Equal(ZipRetentionState.Created,(await f.Retention()).State);
        Assert.Null(await f.Storage.GetReceiptAsync(new(created.Retention.Identity.ReleaseOperationId)));
    }
    [Theory]
    [InlineData("ReleasePending")] [InlineData("PhysicalReleased")] [InlineData("CandidateDeleted")] [InlineData("Released")]
    public async Task Fresh_runtime_reconciles_release_receipts_and_candidate_cleanup(string boundary)
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;await f.Acknowledge();
        var service=f.Service();service.Checkpoint=n=>n==boundary?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.ReleaseAsync(f.Parent,0));await f.Refresh();
        var result=await f.Service().ReleaseAsync(f.Parent,0);Assert.Equal(ZipRetentionState.Released,result.Retention.State);
        Assert.Equal(created.Retention.CreateReceipt!.CurrentRevision,result.Retention.ReleaseReceipt!.PriorRevision);
        Assert.NotEqual(result.Retention.ReleaseReceipt.PriorRevision,result.Retention.ReleaseReceipt.CurrentRevision);
        Assert.False(File.Exists(f.CandidatePath(result.Retention)));
        Assert.Equal(created.Retention.Identity,result.Retention.Identity);Assert.Equal(1,f.Encryption.Encryptions);
    }
    [Fact]
    public async Task Stale_owner_epoch_revision_cannot_create_or_release()
    {
        await using var f=await Fixture.Create();var stale=f.Parent;var next=await f.Journal.ClaimAsync(stale.Binding.OperationId,"owner",TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Service().SealAsync(stale,0,f.Ingestion.Content));Assert.Equal(0,f.Encryption.Encryptions);
        f.Parent=next;var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;await f.Acknowledge();stale=f.Parent;
        next=await f.Journal.ClaimAsync(stale.Binding.OperationId,"owner",TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Service().ReleaseAsync(stale,0));
        Assert.Null(await f.Storage.GetReceiptAsync(new(created.Retention.Identity.ReleaseOperationId)));
        Assert.Equal(next.Fence,(await f.Journal.ReadAsync(stale.Binding.OperationId))!.Fence);
    }
    [Fact]
    public async Task Cancellation_after_staging_clears_encryption_buffers_without_deletion()
    {
        await using var f=await Fixture.Create();using var cancelled=new CancellationTokenSource();var service=f.Service();
        service.Checkpoint=n=>{if(n=="Staged")cancelled.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.SealAsync(f.Parent,0,f.Ingestion.Content,cancelled.Token));
        Assert.NotNull(f.Encryption.Ciphertext);Assert.True(f.Encryption.Ciphertext.All(b=>b==0));
        var r=await f.Retention();Assert.True(File.Exists(f.CandidatePath(r)));Assert.Null(await f.Storage.GetReceiptAsync(new(r.Identity.ReleaseOperationId)));
    }
    private sealed class StorageProbe(IZipPrivateContentStorage inner) : IZipPrivateContentStorage
    {
        public bool MissingReceipt,MissingContent,ThrowDisposal,ChangedNamespace;
        public Action? ReadingReceipt;
        public string NamespaceId=>ChangedNamespace?new string('C',64):inner.NamespaceId;
        public long MaximumProtectedBytes=>inner.MaximumProtectedBytes;
        public Task StageAsync(ArtifactContentOperationId op,ReadOnlyMemory<byte> bytes,CancellationToken ct=default)=>inner.StageAsync(op,bytes,ct);
        public Task<byte[]?> ReadCandidateAsync(ArtifactContentOperationId op,CancellationToken ct=default)=>inner.ReadCandidateAsync(op,ct);
        public Task<ArtifactContentMutationReceipt?> GetReceiptAsync(ArtifactContentOperationId op,CancellationToken ct=default){ ReadingReceipt?.Invoke();return MissingReceipt?Task.FromResult<ArtifactContentMutationReceipt?>(null):inner.GetReceiptAsync(op,ct); }
        public Task<IPreparedArtifactContentMutation> PrepareCreateAsync(ArtifactId id,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.PrepareCreateAsync(id,bytes,context,ct);
        public Task<IPreparedArtifactContentMutation> PrepareDeleteAsync(ArtifactId id,ArtifactContentRevision rev,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.PrepareDeleteAsync(id,rev,context,ct);
        public Task<IZipPreparedCandidateCleanup> PrepareCandidateCleanupAsync(ArtifactContentOperationId op,string hash,CancellationToken ct=default)=>inner.PrepareCandidateCleanupAsync(op,hash,ct);
        public async Task<IArtifactContentReadLease?> ReadProtectedAsync(ArtifactId id,ArtifactContentRevision rev,CancellationToken ct=default)
        { if(MissingContent)return null;var raw=await inner.ReadProtectedAsync(id,rev,ct);return raw is not null&&ThrowDisposal?new ThrowingLease(raw):raw; }
    }
    private sealed class ThrowingLease(IArtifactContentReadLease inner) : IArtifactContentReadLease
    {
        public ArtifactId ArtifactId=>inner.ArtifactId;
        public ArtifactContentRevision Revision=>inner.Revision;
        public long StoredLength=>inner.StoredLength;
        public long ReturnedLength=>inner.ReturnedLength;
        public ReadOnlyMemory<byte> Content=>inner.Content;
        public Stream OpenReadStream()=>inner.OpenReadStream();
        public void Dispose(){inner.Dispose();throw new IOException("Injected raw disposal failure.");}
        public async ValueTask DisposeAsync(){await inner.DisposeAsync();throw new IOException("Injected raw disposal failure.");}
    }
    [Theory]
    [InlineData(true,true)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(false,false)]
    public async Task Missing_created_evidence_fails_closed_and_persists_review(bool missingReceipt,bool seal)
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        var probe=new StorageProbe(f.Storage){MissingReceipt=missingReceipt,MissingContent=!missingReceipt};
        var service=new ZipProtectedRetentionService(f.Journal,f.Journal,probe,f.Encryption);
        if(seal)await Assert.ThrowsAsync<InvalidDataException>(()=>service.SealAsync(f.Parent,0,null));
        else await Assert.ThrowsAsync<InvalidDataException>(()=>service.OpenAsync(f.Parent,0));
        Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);await f.Refresh();Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
        Assert.Equal(1,f.Encryption.Encryptions);Assert.Null(await f.Storage.GetReceiptAsync(new(created.Retention.Identity.ReleaseOperationId)));
    }
    [Fact]
    public async Task Throwing_raw_disposal_never_hands_off_plaintext_and_clears_allocated_output()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        var probe=new StorageProbe(f.Storage){ThrowDisposal=true};var service=new ZipProtectedRetentionService(f.Journal,f.Journal,probe,f.Encryption);
        await Assert.ThrowsAsync<IOException>(()=>service.OpenAsync(f.Parent,0));Assert.NotNull(f.Encryption.Plaintext);
        Assert.Equal(f.Ingestion.Content.Length,f.Encryption.Plaintext.Length);Assert.True(f.Encryption.Plaintext.All(b=>b==0));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Review_parent_cannot_start_or_bind_retention(bool preparing)
    {
        await using var f=await Fixture.Create();var hash=Convert.ToHexString(SHA256.HashData(f.Ingestion.Content));
        var reserved=await f.Journal.ReserveRetentionAsync(f.Parent.Fence,0,f.Storage.NamespaceId,hash);
        var current=preparing?await f.Journal.BeginRetentionPreparationAsync(reserved.Parent.Fence,reserved.Retention):reserved;
        var review=await f.Journal.ReviewAsync(current.Parent.Fence,"TestReview");
        if(preparing)await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Journal.BindRetentionCandidateAsync(review.Fence,current.Retention,new string('A',64)));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Journal.BeginRetentionPreparationAsync(review.Fence,current.Retention));
        Assert.Equal(current.Retention,await f.Retention());Assert.Equal(0,f.Encryption.Encryptions);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Changed_physical_revision_is_never_deleted_or_read_as_the_bound_child(bool release)
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        if(release)await f.Acknowledge();
        var physical=new FileSystemArtifactContentStore(Path.Combine(f.Root,"zip-quarantine"),f.Storage.MaximumProtectedBytes);
        var r=created.Retention;var newer=await physical.ReplaceIfRevisionMatchesAsync(new(r.Identity.ObjectId),r.CreateReceipt!.CurrentRevision!.Value,
            "newer protected generation"u8.ToArray(),new(new("replacement"),new ArtifactContentOwnershipToken(r.Identity.OwnerToken)));
        Assert.Equal(ArtifactContentMutationOutcome.Replaced,newer.Outcome);
        if(release)await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().ReleaseAsync(f.Parent,0));
        else await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().OpenAsync(f.Parent,0));
        Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);
        var retained=await physical.ReadVersionedAsync(new(r.Identity.ObjectId));Assert.NotNull(retained);Assert.Equal(newer.CurrentRevision,retained.Revision);
        var deletion=await f.Storage.GetReceiptAsync(new(r.Identity.ReleaseOperationId));Assert.True(deletion is null||deletion.Outcome==ArtifactContentMutationOutcome.VersionConflict);
    }

    [Fact]
    public async Task Release_namespace_contradiction_persists_review_without_deleting()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;await f.Acknowledge();
        var probe=new StorageProbe(f.Storage){ChangedNamespace=true};var service=new ZipProtectedRetentionService(f.Journal,f.Journal,probe,f.Encryption);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.ReleaseAsync(f.Parent,0));
        Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);Assert.Null(await f.Storage.GetReceiptAsync(new(created.Retention.Identity.ReleaseOperationId)));
    }
    [Fact]
    public async Task Evidence_review_uses_independent_token_after_caller_cancellation()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        using var cancelled=new CancellationTokenSource();var probe=new StorageProbe(f.Storage){MissingReceipt=true,ReadingReceipt=cancelled.Cancel};
        var service=new ZipProtectedRetentionService(f.Journal,f.Journal,probe,f.Encryption);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.OpenAsync(f.Parent,0,cancelled.Token));
        Assert.True(cancelled.IsCancellationRequested);Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);
    }
    [Fact]
    public async Task Cancellation_after_decryption_clears_plaintext_and_never_hands_off_a_lease()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        using var cancelled=new CancellationTokenSource();f.Encryption.AfterDecrypt=cancelled.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Service().OpenAsync(f.Parent,0,cancelled.Token));
        Assert.NotNull(f.Encryption.Plaintext);Assert.True(f.Encryption.Plaintext.All(b=>b==0));
        Assert.Null(await f.Storage.GetReceiptAsync(new(created.Retention.Identity.ReleaseOperationId)));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ownership_moves_after_preparation_and_blocks_physical_mutation(bool release)
    {
        await using var f=await Fixture.Create();var service=f.Service();ZipParentSnapshot? newOwner=null;
        if(release){var created=await service.SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;await f.Acknowledge();}
        service.Checkpoint=async n=>{if(n==(release?"ReleasePending":"CandidateBound")){f.Time.Now+=TimeSpan.FromMinutes(6);newOwner=await f.Journal.ClaimAsync(f.Parent.Binding.OperationId,"new-owner",TimeSpan.FromMinutes(5));}};
        if(release)await Assert.ThrowsAsync<ZipFenceException>(()=>service.ReleaseAsync(f.Parent,0));
        else await Assert.ThrowsAsync<ZipFenceException>(()=>service.SealAsync(f.Parent,0,f.Ingestion.Content));
        var r=await f.Retention();Assert.NotNull(newOwner);
        Assert.Null(await f.Storage.GetReceiptAsync(new(release?r.Identity.ReleaseOperationId:r.Identity.CreateOperationId)));
        Assert.Equal(newOwner.Fence,(await f.Journal.ReadAsync(f.Parent.Binding.OperationId))!.Fence);
    }

    [Fact]
    public async Task Missing_durable_retention_read_marks_parent_review()
    {
        await using var f=await Fixture.Create();await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().OpenAsync(f.Parent,0));
        await f.Refresh();Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(0,f.Encryption.Encryptions);
    }
    [Fact]
    public async Task Missing_committed_generation_marks_review_without_rerandomization()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        var files=Directory.GetFiles(Path.Combine(f.Root,"zip-quarantine",".content-generations"));Assert.Single(files);File.Delete(files[0]);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().SealAsync(f.Parent,0,null));
        Assert.Equal(ZipRetentionState.RequiresReview,(await f.Retention()).State);Assert.Equal(1,f.Encryption.Encryptions);
    }

    [Fact]
    public async Task Retention_identity_and_all_frozen_evidence_reject_sql_mutation()
    {
        await using var f=await Fixture.Create();var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;
        foreach(var sql in new[]{
            "UPDATE ZipExtractionRetentions SET BindingJson='{}'",
            "DELETE FROM ZipExtractionRetentions",
            "INSERT OR REPLACE INTO ZipExtractionRetentions SELECT * FROM ZipExtractionRetentions",
            "UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Revision',json_extract(StateJson,'$.Revision')+1,'$.CandidateHash','changed')",
            "UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Revision',json_extract(StateJson,'$.Revision')+1,'$.CreateReceipt',NULL)"})
            await Assert.ThrowsAsync<SqliteException>(()=>f.Sql(sql));
        Assert.Equal(created.Retention,await f.Retention());await f.Acknowledge();var released=await f.Service().ReleaseAsync(f.Parent,0);
        foreach(var field in new[]{"ReleaseReceipt","CandidateCleanup"})
            await Assert.ThrowsAsync<SqliteException>(()=>f.Sql("UPDATE ZipExtractionRetentions SET StateJson=json_set(StateJson,'$.Revision',json_extract(StateJson,'$.Revision')+1,'$."+field+"',NULL)"));
        Assert.Equal(released.Retention,await f.Retention());
    }

    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")]
    public async Task Every_parent_fence_component_independently_blocks_create_and_release(string component)
    {
        foreach(var release in new[]{false,true})
        {
            await using var f=await Fixture.Create();ZipRetentionRecord? retained=null;
            if(release){var created=await f.Service().SealAsync(f.Parent,0,f.Ingestion.Content);f.Parent=created.Parent;retained=created.Retention;await f.Acknowledge();}
            var fence=f.Parent.Fence;
            fence=component switch{"owner"=>fence with{Owner="stale"},"epoch"=>fence with{Epoch=fence.Epoch-1},_=>fence with{Revision=fence.Revision-1}};
            var stale=f.Parent with{Fence=fence};
            if(release)await Assert.ThrowsAsync<ZipFenceException>(()=>f.Service().ReleaseAsync(stale,0));
            else await Assert.ThrowsAsync<ZipFenceException>(()=>f.Service().SealAsync(stale,0,f.Ingestion.Content));
            Assert.Equal(release?1:0,f.Encryption.Encryptions);
            if(retained is not null)Assert.Null(await f.Storage.GetReceiptAsync(new(retained.Identity.ReleaseOperationId)));
            Assert.Equal(f.Parent.Fence,(await f.Journal.ReadAsync(f.Parent.Binding.OperationId))!.Fence);
        }
    }

}
