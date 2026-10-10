using System.Text.Json;
using EMF.ConsoleApplication;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Ingestion;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;
public sealed class ZipChildIngestionTests
{
    private sealed class Crash:Exception;
    private sealed class MissingCandidate(IArtifactContentStagingStore inner):IArtifactContentStagingStore
    {
        public Task StageAsync(ArtifactContentOperationId id,ReadOnlyMemory<byte> bytes,CancellationToken ct=default)=>inner.StageAsync(id,bytes,ct);
        public Task<byte[]?> ReadAsync(ArtifactContentOperationId id,CancellationToken ct=default)=>Task.FromResult<byte[]?>(null);
    }
    private sealed class HookStore(IPreparedArtifactContentStore inner):IPreparedArtifactContentStore
    {
        internal Func<Task>? Prepared,Read;internal int Creates,Writes;
        public async Task<IPreparedArtifactContentMutation> PreparePhysicalCreateAsync(ArtifactId id,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)
        {var handle=await inner.PreparePhysicalCreateAsync(id,bytes,context,ct);try{if(Prepared is not null)await Prepared();return new Mutation(handle,this);}catch{await handle.DisposeAsync();throw;}}
        private sealed class Mutation(IPreparedArtifactContentMutation inner,HookStore owner):IPreparedArtifactContentMutation
        {public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct=default){owner.Creates++;return await inner.ExecuteAsync(ct);}public ValueTask DisposeAsync()=>inner.DisposeAsync();}
        public Task<IPreparedArtifactContentMutation> PreparePhysicalDeleteAsync(ArtifactId id,ArtifactContentRevision rev,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.PreparePhysicalDeleteAsync(id,rev,context,ct);
        public Task<IArtifactContentRevisionProbe> PrepareRevisionValidationAsync(ArtifactId id,CancellationToken ct=default)=>inner.PrepareRevisionValidationAsync(id,ct);
        public Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(ArtifactId id,CancellationToken ct=default)=>inner.ReadCurrentRevisionAsync(id,ct);
        public async Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id,CancellationToken ct=default){var result=await inner.ReadVersionedAsync(id,ct);if(Read is not null)await Read();return result;}
        public Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)=>throw new InvalidOperationException("Prepared capability required.");
        public Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id,ArtifactContentRevision rev,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.ReplaceIfRevisionMatchesAsync(id,rev,bytes,context,ct);
        public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id,ArtifactContentRevision rev,ArtifactContentMutationContext context,CancellationToken ct=default)=>inner.DeleteIfRevisionMatchesAsync(id,rev,context,ct);
        public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId id,CancellationToken ct=default)=>inner.GetMutationOutcomeAsync(id,ct);
        public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(ArtifactContentReceiptCursor? after,int limit,CancellationToken ct=default)=>inner.ReadAuditObligationsAsync(after,limit,ct);
        public Task WriteAsync(ArtifactId id,ReadOnlyMemory<byte> bytes,CancellationToken ct=default){Writes++;throw new InvalidOperationException("Direct child WriteAsync forbidden.");}
        public Task DeleteAsync(ArtifactId id,CancellationToken ct=default)=>throw new InvalidOperationException("Direct delete forbidden.");
        public Task<byte[]?> ReadAsync(ArtifactId id,CancellationToken ct=default)=>inner.ReadAsync(id,ct);
    }
    private sealed class Fixture:IAsyncDisposable
    {
        internal ZipChildScanTests.Fixture Slice=null!;internal HookStore Physical=null!;internal int IngestCalls,ResumeCalls;
        internal ArtifactIngestionFixture Evidence=>Slice.Evidence;internal SqliteZipExtractionJournal Journal=>Slice.Journal;
        internal ZipParentSnapshot Parent{get=>Slice.Parent;set=>Slice.Parent=value;}
        internal ZipEntryProgress Entry=>Parent.Entries[0];
        internal static async Task<Fixture> Create(bool clean=true)
        {
            var f=new Fixture{Slice=await ZipChildScanTests.Fixture.Create()};f.Physical=new(f.Evidence.Physical);
            if(clean)f.Parent=await f.Slice.Service(new ZipChildScanTests.Scanner()).ScanNextAsync(f.Parent);
            f.Evidence.SecurityContext.Operation=new(new(f.Entry.Plan.ChildOperationId!),f.Evidence.SecurityContext.Operation.AuthorizedOperationId,"synthetic-child-actor",new("zip"));return f;
        }
        internal ZipChildIngestionHost Host(IArtifactContentStagingStore? staging=null,bool fresh=false,IAcknowledgedSecurityAuditSink? audit=null)=>new(Evidence.DatabasePath,
            fresh?new FileSystemArtifactContentStore(Path.Combine(Evidence.Root,"content")):Physical,
            staging??new FileSystemArtifactContentStagingStore(Path.Combine(Evidence.Root,"staging")),Evidence.Encryption,
            (_,ct)=>Task.FromResult<IArtifactIngestionSecurityContext>(Evidence.SecurityContext),Evidence.Classification,Evidence.Authorization,audit??Evidence.Audit,Evidence.Fingerprints);
        internal ZipChildIngestionService Service(bool missingHost=false,IArtifactContentStagingStore? staging=null,bool fresh=false,IAcknowledgedSecurityAuditSink? audit=null)
        {
            var s=new ZipChildIngestionService(Journal,Journal,Journal,Slice.Retention(),missingHost?null:Host(staging,fresh,audit));
            s.Checkpoint=n=>{if(n=="Ingesting")IngestCalls++;if(n=="Resuming")ResumeCalls++;return Task.CompletedTask;};return s;
        }
        internal IngestionMetadataDraft Draft(ArtifactId id)=>new(new Artifact{Id=id,Name="fixture.txt",ArtifactType="file",Fingerprint=new(){Algorithm="SHA-256",Value=Entry.Retained!.Sha256}},
            new Provenance{ArtifactId=id,Source="zip-parent:"+Parent.Binding.ParentArtifactId+"/entry:"+Entry.Plan.CentralOrdinal,RecordedBy="fixture"});
        internal async Task SeedCanonical()
        {
            var saved=Evidence.SecurityContext.Operation;Evidence.SecurityContext.Operation=new(ArtifactContentOperationId.New(),new(Guid.NewGuid().ToString("N")),"fixture-canonical-actor");
            await using var lease=await Slice.Retention().OpenAsync(Parent,0);await Evidence.Service().IngestAsync(Draft(Evidence.Id),lease.Content);Evidence.SecurityContext.Operation=saved;
        }
        internal async Task<long> Count(string table)
        {await using var c=new SqliteConnection("Data Source="+Evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM "+table;return Convert.ToInt64(await q.ExecuteScalarAsync());}
        internal Task Refresh()=>Slice.Refresh();
        public ValueTask DisposeAsync()=>Slice.DisposeAsync();
    }
    [Fact]
    public async Task New_operation_uses_persisted_ids_and_IngestAsync_without_relationship_ack_or_direct_write()
    {
        await using var f=await Fixture.Create();var plan=f.Entry.Plan;f.Parent=await f.Service().IngestNextAsync(f.Parent);
        Assert.Equal(1,f.IngestCalls);Assert.Equal(0,f.ResumeCalls);Assert.Equal(1,f.Physical.Creates);Assert.Equal(0,f.Physical.Writes);
        Assert.Equal(ZipEntryState.Ingested,f.Entry.State);Assert.Equal(plan.ChildOperationId,f.Entry.Ingestion!.ChildOperationId);Assert.Equal(plan.ProvisionalArtifactId,f.Entry.Ingestion.CanonicalArtifactId);
        Assert.Equal(1,f.Parent.Budget.IngestionAttempts);Assert.Equal(-1,f.Parent.Fence.ConfirmedOrdinal);Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(1,await f.Count("Artifacts"));Assert.True(f.Slice.Encryption.Plaintext!.All(b=>b==0));
        Assert.Equal(ZipRetentionState.Created,(await f.Journal.ReadRetentionAsync("zip",0))!.State);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Lost_response_after_adoption_or_dedup_resumes_original_operation_without_republication(bool dedup)
    {
        await using var f=await Fixture.Create();if(dedup)await f.SeedCanonical();var id=f.Entry.Plan.ChildOperationId;
        var service=f.Service();service.Checkpoint=n=>n=="CoordinatorReturned"?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.IngestNextAsync(f.Parent));await f.Refresh();var encrypted=f.Evidence.Encryption.Encryptions;var revision=await f.Physical.ReadCurrentRevisionAsync(new(f.Entry.Plan.ProvisionalArtifactId!));
        f.Parent=await f.Service(fresh:true).IngestNextAsync(f.Parent);
        Assert.Equal(0,f.IngestCalls);Assert.Equal(1,f.ResumeCalls);Assert.Equal(encrypted,f.Evidence.Encryption.Encryptions);Assert.Equal(id,f.Entry.Ingestion!.ChildOperationId);Assert.Equal(1,f.Physical.Creates);Assert.Equal(2,f.Parent.Budget.IngestionAttempts);
        Assert.Equal(dedup?f.Evidence.Id.Value:f.Entry.Plan.ProvisionalArtifactId,f.Entry.Ingestion.CanonicalArtifactId);Assert.Equal(dedup?ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact:ArtifactIngestionDisposition.ProvisionalArtifactAdopted,f.Entry.Ingestion.Disposition);
        await using(var s=await f.Evidence.Persistence.AcquireAsync(new(id!)))Assert.Equal(!dedup,await s.HasAdoptionEvidenceAsync(new(f.Entry.Plan.ProvisionalArtifactId!)));
        Assert.Equal(revision,await f.Physical.ReadCurrentRevisionAsync(new(f.Entry.Plan.ProvisionalArtifactId!)));Assert.Equal(1,await f.Count("Artifacts"));Assert.Equal(0,await f.Count("Relationships"));Assert.Equal(-1,f.Parent.Fence.ConfirmedOrdinal);
    }
    [Fact]
    public async Task Recorded_result_reentry_still_uses_Resume_and_spends_no_new_publication_attempt()
    {
        await using var f=await Fixture.Create();f.Parent=await f.Service().IngestNextAsync(f.Parent);var before=f.Parent;var encryption=f.Evidence.Encryption.Encryptions;
        f.Parent=await f.Service(fresh:true).IngestNextAsync(f.Parent);Assert.Equal(1,f.ResumeCalls);Assert.Equal(before.Fence,f.Parent.Fence);Assert.Equal(before.Budget,f.Parent.Budget);Assert.Equal(encryption,f.Evidence.Encryption.Encryptions);Assert.Equal(1,f.Physical.Creates);
    }
    [Fact]
    public async Task Reservation_crash_without_generic_intent_retries_initial_ingest_with_same_ids()
    {
        await using var f=await Fixture.Create();var child=f.Entry.Plan.ChildOperationId;var service=f.Service();service.Checkpoint=n=>n=="Reserved"?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.IngestNextAsync(f.Parent));await f.Refresh();Assert.Equal(0,await f.Count("ArtifactIngestionIntents"));f.Parent=await f.Service().IngestNextAsync(f.Parent);Assert.Equal(1,f.IngestCalls);Assert.Equal(0,f.ResumeCalls);Assert.Equal(child,f.Entry.Ingestion!.ChildOperationId);Assert.Equal(2,f.Parent.Budget.IngestionAttempts);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Existing_prepared_intent_without_candidate_resumes_and_never_regenerates(bool preparationStarted)
    {
        await using var f=await Fixture.Create();
        if(preparationStarted)
        {
            f.Evidence.Encryption.EncryptHook=()=>throw new Crash();await Assert.ThrowsAsync<Crash>(()=>f.Service().IngestNextAsync(f.Parent));f.Evidence.Encryption.EncryptHook=null;await f.Refresh();
        }
        else
        {
            await using var lease=await f.Slice.Retention().OpenAsync(f.Parent,0);
            await Assert.ThrowsAsync<ArtifactIngestionFixture.SimulatedCrashException>(()=>f.Evidence.Service(persistence:new ArtifactIngestionFixture.FaultPersistence(f.Evidence.Persistence,"Prepared")).IngestAsync(f.Draft(new(f.Entry.Plan.ProvisionalArtifactId!)),lease.Content));
        }
        var encrypted=f.Evidence.Encryption.Encryptions;await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().IngestNextAsync(f.Parent));await f.Refresh();
        Assert.Equal(1,f.ResumeCalls);Assert.Equal(preparationStarted?1:0,f.IngestCalls);Assert.Equal(encrypted,f.Evidence.Encryption.Encryptions);Assert.Equal(0,f.Physical.Creates);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(0,await f.Count("Artifacts"));
    }
    [Fact]
    public async Task Bound_candidate_missing_on_restart_requires_review_without_new_ciphertext()
    {
        await using var f=await Fixture.Create();f.Physical.Prepared=()=>throw new Crash();await Assert.ThrowsAsync<Crash>(()=>f.Service().IngestNextAsync(f.Parent));f.Physical.Prepared=null;await f.Refresh();
        await using(var s=await f.Evidence.Persistence.AcquireAsync(new(f.Entry.Plan.ChildOperationId!)))Assert.NotNull(s.Intent!.CandidateHash);
        var encrypted=f.Evidence.Encryption.Encryptions;await Assert.ThrowsAnyAsync<Exception>(()=>f.Service(staging:new MissingCandidate(f.Evidence.Staging)).IngestNextAsync(f.Parent));await f.Refresh();
        Assert.Equal(1,f.ResumeCalls);Assert.Equal(encrypted,f.Evidence.Encryption.Encryptions);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(0,f.Physical.Creates);
    }
    [Theory]
    [InlineData("owner")] [InlineData("epoch")] [InlineData("revision")] [InlineData("frontier")]
    public async Task Each_stale_fence_component_blocks_generic_session_and_releases_writer(string component)
    {
        await using var f=await Fixture.Create();var fence=f.Parent.Fence;fence=component switch{"owner"=>fence with{Owner="stale"},"epoch"=>fence with{Epoch=fence.Epoch-1},"revision"=>fence with{Revision=fence.Revision-1},_=>fence with{ConfirmedOrdinal=0}};
        var persistence=new ZipFencedIngestionPersistence(f.Evidence.DatabasePath,fence,f.Entry);await Assert.ThrowsAsync<ZipFenceException>(()=>persistence.AcquireAsync(new(f.Entry.Plan.ChildOperationId!)));
        await f.Slice.Sql("BEGIN IMMEDIATE;ROLLBACK;");Assert.Equal(0,await f.Count("ArtifactIngestionIntents"));
    }
    [Theory]
    [InlineData("encryption")] [InlineData("prepared")] [InlineData("adoption")]
    public async Task Ownership_moved_during_detached_work_blocks_create_or_adoption_and_original_operation_can_resume(string point)
    {
        await using var f=await Fixture.Create();async Task Move(){await f.Slice.Sql("BEGIN IMMEDIATE;ROLLBACK;UPDATE ZipExtractionParents SET OwnerUntil='2000-01-01T00:00:00.0000000+00:00';");await f.Journal.ClaimAsync("zip","new-owner",TimeSpan.FromMinutes(5));}
        if(point=="encryption")f.Evidence.Encryption.EncryptHook=Move;else if(point=="prepared")f.Physical.Prepared=Move;else f.Physical.Read=Move;
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Service().IngestNextAsync(f.Parent));Assert.Equal(point=="adoption"?1:0,f.Physical.Creates);Assert.Equal(0,await f.Count("Artifacts"));
        f.Evidence.Encryption.EncryptHook=null;f.Physical.Prepared=null;f.Physical.Read=null;await f.Refresh();var encryption=f.Evidence.Encryption.Encryptions;
        Assert.Equal("new-owner",f.Parent.Fence.Owner);f.Parent=await f.Service().IngestNextAsync(f.Parent);Assert.Equal(1,f.ResumeCalls);Assert.Equal(encryption,f.Evidence.Encryption.Encryptions);Assert.Equal(1,f.Physical.Creates);Assert.Equal(ZipEntryState.Ingested,f.Entry.State);
    }
    [Fact]
    public async Task Missing_authoritative_host_fails_closed_without_ingestion_reservation()
    {
        Assert.Null(ZipRuntimeComposition.Default.ChildIngestionHost);await using var f=await Fixture.Create();await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service(missingHost:true).IngestNextAsync(f.Parent));await f.Refresh();Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(0,f.Parent.Budget.IngestionAttempts);Assert.Equal(0,f.Physical.Creates);
    }
    [Theory]
    [InlineData("operation")] [InlineData("parent")] [InlineData("classification")]
    public async Task Host_cannot_manufacture_missing_or_wrong_authority(string part)
    {
        await using var f=await Fixture.Create();if(part=="classification")f.Evidence.Classification.Classification=null;else f.Evidence.SecurityContext.Operation=part=="operation"?f.Evidence.SecurityContext.Operation with{OperationId=ArtifactContentOperationId.New()}:f.Evidence.SecurityContext.Operation with{ParentOperationId=new("other")};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service().IngestNextAsync(f.Parent));await f.Refresh();Assert.Equal(0,f.Physical.Creates);Assert.Equal(0,await f.Count("Artifacts"));Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
    }
    [Theory]
    [InlineData(MalwareCoverage.Unknown)] [InlineData(MalwareCoverage.Partial)]
    public async Task Noncomplete_scan_cannot_reach_ingestion(MalwareCoverage coverage)
    {
        await using var f=await Fixture.Create(clean:false);f.Parent=await f.Slice.Service(new ZipChildScanTests.Scanner{Coverage=coverage,Failure=MalwareScanFailure.CoverageUnproven}).ScanNextAsync(f.Parent);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service().IngestNextAsync(f.Parent));Assert.Equal(0,f.Parent.Budget.IngestionAttempts);Assert.Equal(0,f.Physical.Creates);
    }
    [Fact]
    public async Task Changed_latest_scan_evidence_blocks_generic_boundary_even_if_snapshot_says_clean()
    {
        await using var f=await Fixture.Create();var old=f.Entry;await f.Slice.Sql("UPDATE ZipExtractionEntries SET ProgressJson=json_set(ProgressJson,'$.EvidenceJson','{}')");
        var p=new ZipFencedIngestionPersistence(f.Evidence.DatabasePath,f.Parent.Fence,old);await Assert.ThrowsAsync<InvalidDataException>(()=>p.AcquireAsync(new(old.Plan.ChildOperationId!)));Assert.Equal(0,f.Physical.Creates);await f.Slice.Sql("BEGIN IMMEDIATE;ROLLBACK;");
    }
    [Theory]
    [InlineData("review")] [InlineData("audit")] [InlineData("artifact")] [InlineData("ownership")] [InlineData("receipt")]
    public async Task Recorded_proof_fails_closed_if_generic_evidence_becomes_missing_or_contradictory(string part)
    {
        await using var f=await Fixture.Create();f.Parent=await f.Service().IngestNextAsync(f.Parent);var id=f.Entry.Plan.ChildOperationId;
        await f.Slice.Sql(part switch{"review"=>"INSERT INTO ArtifactIngestionReviews VALUES('"+id+"','Contradiction','fixture',NULL)","audit"=>"DELETE FROM ArtifactIngestionAudit WHERE OperationId='"+id+"'","receipt"=>"UPDATE ArtifactIngestionIntents SET IntentJson=json_set(IntentJson,'$.CreateReceipt.ArtifactId.Value','foreign') WHERE OperationId='"+id+"'","ownership"=>"UPDATE ArtifactIngestionIntents SET OwnershipToken='wrong' WHERE OperationId='"+id+"'",_=>"UPDATE ArtifactIngestionIntents SET ArtifactId='wrong' WHERE OperationId='"+id+"'"});
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));
    }
    [Theory]
    [InlineData(MalwareDetection.MalwareDetected,MalwareScanFailure.Threat)]
    [InlineData(MalwareDetection.Error,MalwareScanFailure.TransportError)]
    public async Task Complete_coverage_alone_cannot_authorize_ingestion(MalwareDetection detection,MalwareScanFailure failure)
    {
        await using var f=await Fixture.Create(clean:false);f.Parent=await f.Slice.Service(new ZipChildScanTests.Scanner{Detection=detection,Coverage=MalwareCoverage.Complete,Failure=failure}).ScanNextAsync(f.Parent);
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Service().IngestNextAsync(f.Parent));Assert.Equal(0,f.Physical.Creates);Assert.Equal(0,await f.Count("ArtifactIngestionIntents"));
    }
    [Fact]
    public async Task Repeated_reservation_crashes_remain_charged_and_third_publication_attempt_rejects()
    {
        await using var f=await Fixture.Create();for(var n=0;n<2;n++){var service=f.Service();service.Checkpoint=point=>point=="Reserved"?throw new Crash():Task.CompletedTask;await Assert.ThrowsAsync<Crash>(()=>service.IngestNextAsync(f.Parent));await f.Refresh();}
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().IngestNextAsync(f.Parent));await f.Refresh();Assert.Equal(2,f.Parent.Budget.IngestionAttempts);Assert.Equal(0,f.Physical.Creates);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
    }
    [Fact]
    public async Task Cancellation_clears_owned_plaintext_and_existing_intent_is_resumed_without_regeneration()
    {
        await using var f=await Fixture.Create();using var ct=new CancellationTokenSource();f.Evidence.Encryption.EncryptHook=()=>{ct.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Service().IngestNextAsync(f.Parent,ct.Token));await f.Refresh();Assert.True(f.Slice.Encryption.Plaintext!.All(b=>b==0));Assert.Equal(1,f.Parent.Budget.IngestionAttempts);
        f.Evidence.Encryption.EncryptHook=null;var encrypted=f.Evidence.Encryption.Encryptions;await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service().IngestNextAsync(f.Parent));Assert.Equal(1,f.ResumeCalls);Assert.Equal(encrypted,f.Evidence.Encryption.Encryptions);Assert.Equal(0,f.Physical.Creates);
    }

    [Fact]
    public async Task Same_database_writer_lock_excludes_ownership_change_until_generic_session_disposes()
    {
        await using var f=await Fixture.Create();var p=new ZipFencedIngestionPersistence(f.Evidence.DatabasePath,f.Parent.Fence,f.Entry);
        var session=await p.AcquireAsync(new(f.Entry.Plan.ChildOperationId!));var moved=Task.Run(()=>f.Journal.ClaimAsync("zip","owner",TimeSpan.FromMinutes(5)));
        try{await Task.Delay(100);Assert.False(moved.IsCompleted);}finally{await session.DisposeAsync();}
        var next=await moved;Assert.True(next.Fence.Epoch>f.Parent.Fence.Epoch);await Assert.ThrowsAsync<ZipFenceException>(()=>p.AcquireAsync(new(f.Entry.Plan.ChildOperationId!)));
    }

    private sealed class UnavailableAudit:IAcknowledgedSecurityAuditSink
    {
        public Task WriteAsync(SecurityAuditRecord record,CancellationToken ct=default)=>throw new IOException("Audit unavailable.");
        public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record,CancellationToken ct=default)=>throw new IOException("Audit unavailable.");
        public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id,CancellationToken ct=default)=>throw new IOException("Audit unavailable.");
    }
    [Fact]
    public async Task Recorded_result_resume_rechecks_canonical_audit_instead_of_trusting_delivery_hint()
    {
        await using var f=await Fixture.Create();f.Parent=await f.Service().IngestNextAsync(f.Parent);var attempts=f.Parent.Budget.IngestionAttempts;
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Service(audit:new UnavailableAudit()).IngestNextAsync(f.Parent));Assert.Equal(1,f.ResumeCalls);Assert.Equal(1,f.Physical.Creates);Assert.Equal(attempts,f.Parent.Budget.IngestionAttempts);Assert.Equal(0,await f.Count("Relationships"));
    }
    [Fact]
    public async Task Cancellation_after_adoption_before_zip_record_resumes_without_new_publication()
    {
        await using var f=await Fixture.Create();using var ct=new CancellationTokenSource();var service=f.Service();service.Checkpoint=n=>{if(n=="CoordinatorReturned")ct.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.IngestNextAsync(f.Parent,ct.Token));await f.Refresh();Assert.Equal(ZipEntryState.Scanned,f.Entry.State);Assert.Equal(1,await f.Count("Artifacts"));Assert.True(f.Slice.Encryption.Plaintext!.All(b=>b==0));
        var encrypted=f.Evidence.Encryption.Encryptions;f.Parent=await f.Service(fresh:true).IngestNextAsync(f.Parent);Assert.Equal(1,f.ResumeCalls);Assert.Equal(encrypted,f.Evidence.Encryption.Encryptions);Assert.Equal(1,f.Physical.Creates);Assert.Equal(2,f.Parent.Budget.IngestionAttempts);
    }

    [Theory]
    [InlineData("revision")] [InlineData("fingerprint")]
    public async Task Exact_retained_binding_is_revalidated_by_generic_persistence_boundary(string part)
    {
        await using var f=await Fixture.Create();var retained=f.Entry.Retained!;var wrong=f.Entry with{Retained=part=="revision"?retained with{Revision="different"}:retained with{Sha256=new string('A',64)}};
        var p=new ZipFencedIngestionPersistence(f.Evidence.DatabasePath,f.Parent.Fence,wrong);await Assert.ThrowsAsync<InvalidDataException>(()=>p.AcquireAsync(new(f.Entry.Plan.ChildOperationId!)));Assert.Equal(0,f.Physical.Creates);await f.Slice.Sql("BEGIN IMMEDIATE;ROLLBACK;");
    }

}
