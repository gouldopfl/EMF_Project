using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;
using EMF.Security.Storage;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerCaptureContractTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "emf-reviewer-contract-" + Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Root,"metadata.sqlite");
        public string SourceRoot => Path.Combine(Root,"source");
        public ArtifactId Artifact { get; } = new("synthetic");
        public OperationSnapshotId Snapshot { get; } = OperationSnapshotId.New();
        public byte[] Bytes { get; } = Encoding.UTF8.GetBytes("synthetic frozen input");
        public FileSystemArtifactContentStore Physical { get; private set; } = null!;
        public EncryptedArtifactContentStore Secured { get; private set; } = null!;
        public IEnvelopeEncryptionService Encryption { get; private set; } = null!;
        public FileSystemArtifactContentStagingStore Staging { get; private set; } = null!;
        public ProtectedReviewerRetainedMaterialStore Retained => new(Staging,Encryption);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); Directory.CreateDirectory(f.Root);
            f.Encryption = new DevelopmentEnvelopeEncryptionService(new InMemoryEncryptionKeyProvider(new[]
            { new EncryptionKey { KeyId="synthetic", KeyMaterial=new byte[32] } }));
            f.Physical = new(f.SourceRoot); f.Secured = new(f.Physical,f.Encryption);
            f.Staging = new(Path.Combine(f.Root,"retained"));
            await f.Secured.WriteAsync(f.Artifact,f.Bytes);
            // Test-only fixture DDL. Production capture only opens an existing DB read-only.
            await f.Sql("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE Artifacts(Id TEXT PRIMARY KEY, Name TEXT, ArtifactType TEXT, FingerprintAlgorithm TEXT, FingerprintValue TEXT);
                CREATE TABLE Provenance(Id INTEGER PRIMARY KEY, ArtifactId TEXT, Source TEXT);
                CREATE TABLE Relationships(Id INTEGER PRIMARY KEY, SourceArtifactId TEXT, TargetArtifactId TEXT);
                CREATE TABLE ArtifactMutationAuthority(ArtifactId TEXT PRIMARY KEY,ClassificationId TEXT,ClassificationRevision TEXT,IsAdopted INTEGER);
                INSERT INTO Artifacts VALUES('synthetic','before','text/plain','SHA-256',$hash);
                INSERT INTO Provenance VALUES(1,'synthetic','before');
                INSERT INTO ArtifactMutationAuthority VALUES('synthetic','Confidential','classification-1',1);
                """, ReviewerRetainedValidator.Hash(f.Bytes));
            return f;
        }
        public async Task Sql(string sql,string? hash=null)
        {
            await using var c = new SqliteConnection($"Data Source={Database};Pooling=False"); await c.OpenAsync();
            await using var command = c.CreateCommand(); command.CommandText=sql;
            if(hash is not null) command.Parameters.AddWithValue("$hash",hash);
            await command.ExecuteNonQueryAsync();
        }
        public Task<ReviewerCaptureSession> Open(ReviewerCaptureLimits? limits=null,CancellationToken ct=default,
            Func<string,CancellationToken,Task>? checkpoint=null, IVersionedArtifactContentStore? source=null) =>
            ReviewerCaptureSession.OpenAsync(Database,Snapshot,[Artifact],source??Secured,limits??new(TimeSpan.FromSeconds(10)),ct,checkpoint);
        public async Task<ReviewerCapturedBundle> Capture() { await using var s=await Open(); return await s.CaptureAsync(); }
        public async Task AssertUsable()
        {
            await Sql("UPDATE Artifacts SET Name=Name");
            await using var s=await Open(); Assert.NotNull(await s.CaptureAsync());
            Assert.NotNull(await Secured.ReadVersionedAsync(Artifact));
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root,true); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task Coherent_metadata_view_survives_concurrent_commit_and_is_explicitly_unversioned()
    {
        await using var f=await Fixture.Create(); await using var session=await f.Open();
        await f.Sql("UPDATE Artifacts SET Name='after'; UPDATE Provenance SET Source='after'; UPDATE ArtifactMutationAuthority SET ClassificationRevision='classification-2'");
        var bundle=await session.CaptureAsync();
        var metadata=Assert.Single(bundle.Manifest.Metadata);
        Assert.Null(metadata.SourceRevision); Assert.Contains("before",metadata.Representation); Assert.DoesNotContain("after",metadata.Representation);
        Assert.Equal("classification-1",Assert.Single(bundle.Manifest.Members).ClassificationRevision);
        Assert.Contains("synthetic:relationships",bundle.Manifest.ExplicitAbsences);
        await f.AssertUsable();
    }
    [Fact]
    public async Task Exact_secured_bytes_and_physical_revision_are_copied_and_equal_hashes_do_not_replace_revision()
    {
        await using var f=await Fixture.Create(); var first=await f.Capture();
        Assert.Equal((await f.Physical.ReadVersionedAsync(f.Artifact))!.Revision.Value,first.Manifest.Members[0].PhysicalRevision);
        Assert.Equal(f.Bytes,first.Content[f.Artifact.Value]);
        await f.Secured.WriteAsync(f.Artifact,f.Bytes); var second=await f.Capture();
        Assert.Equal(first.Manifest.Members[0].Sha256,second.Manifest.Members[0].Sha256);
        Assert.NotEqual(first.Manifest.Members[0].PhysicalRevision,second.Manifest.Members[0].PhysicalRevision);
        var reference=await f.Retained.RetainAsync(first);
        var changed=first with { Manifest=first.Manifest with { Members=second.Manifest.Members } };
        await ReplaceRetained(f,changed);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Retained.ReadValidatedAsync(reference));
    }
    [Theory]
    [InlineData("Acquiring")]
    [InlineData("Metadata")]
    [InlineData("Copying")]
    [InlineData("Source")]
    public async Task Overall_deadline_terminates_each_phase_and_releases_owned_resources(string phase)
    {
        await using var f=await Fixture.Create();
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Block(CancellationToken ct) { reached.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); }
        Task Hook(string p,CancellationToken ct)=>p==phase?Block(ct):Task.CompletedTask;
        var source=phase=="Source"?new SourceProxy(f.Secured,Block):null;
        var task=Task.Run(async()=> { await using var s=await f.Open(new(TimeSpan.FromMilliseconds(200)),checkpoint:Hook,source:source); await s.CaptureAsync(); });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task.WaitAsync(TimeSpan.FromSeconds(5)));
        await f.AssertUsable();
    }
    [Theory]
    [InlineData("bytes")]
    [InlineData("metadata")]
    [InlineData("work")]
    public async Task Aggregate_bounds_reject_capture_and_release_resources(string bound)
    {
        await using var f=await Fixture.Create();
        var limits=new ReviewerCaptureLimits(TimeSpan.FromSeconds(10),MaximumSourceBytes:bound=="bytes"?1:1024,
            MaximumMetadataBytes:bound=="metadata"?1:10000,MaximumWork:bound=="work"?1:1000);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>{ await using var s=await f.Open(limits); await s.CaptureAsync(); }); await f.AssertUsable();
    }
    [Fact]
    public async Task Member_bound_and_unsupported_closure_are_rejected_before_complete_result()
    {
        await using var f=await Fixture.Create();
        await Assert.ThrowsAsync<InvalidDataException>(()=>ReviewerCaptureSession.OpenAsync(f.Database,f.Snapshot,[f.Artifact,new("second")],f.Secured,new(TimeSpan.FromSeconds(5),MaximumMembers:1)));
        await f.Sql("INSERT INTO Relationships VALUES(1,'synthetic','outside')");
        await using var s=await f.Open(); await Assert.ThrowsAsync<NotSupportedException>(()=>s.CaptureAsync());
        await f.Sql("DELETE FROM Relationships"); await f.AssertUsable();
    }
    [Theory]
    [InlineData("Metadata")]
    [InlineData("Copying")]
    [InlineData("Source")]
    public async Task Capture_cancellation_at_barrier_disposes_session(string phase)
    {
        await using var f=await Fixture.Create(); using var cts=new CancellationTokenSource();
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Block(CancellationToken ct) { reached.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); }
        var task=Task.Run(async()=>{ await using var s=await f.Open(ct:cts.Token,
            checkpoint:(p,ct)=>p==phase?Block(ct):Task.CompletedTask,
            source:phase=="Source"?new SourceProxy(f.Secured,Block):null); await s.CaptureAsync(); });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5)); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task); await f.AssertUsable();
    }
    [Theory]
    [InlineData("encrypt")]
    [InlineData("stage")]
    [InlineData("validate")]
    public async Task Retention_and_validation_cancellation_publish_no_successful_result(string phase)
    {
        await using var f=await Fixture.Create(); var bundle=await f.Capture();
        var reference=phase=="validate"?await f.Retained.RetainAsync(bundle):null;
        using var cts=new CancellationTokenSource(); var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Block(CancellationToken ct) { reached.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); }
        var encryption=new ProtectionProxy(f.Encryption,phase=="encrypt"?Block:null,phase=="validate"?Block:null);
        var staging=new StagingProxy(f.Staging,phase=="stage"?Block:null);
        var store=new ProtectedReviewerRetainedMaterialStore(staging,encryption);
        Task task=phase=="validate"?store.ReadValidatedAsync(reference!,cts.Token):store.RetainAsync(bundle,cts.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5)); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
        if(phase!="validate") Assert.Empty(Directory.GetFiles(Path.Combine(f.Root,"retained"),"*.candidate"));
        else Assert.NotNull(await f.Retained.ReadValidatedAsync(reference!));
        await f.AssertUsable();
    }
    [Theory]
    [InlineData("missing-member")]
    [InlineData("missing-manifest")]
    [InlineData("truncated")]
    [InlineData("bytes")]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("snapshot")]
    [InlineData("revision")]
    [InlineData("version")]
    [InlineData("reference")]
    [InlineData("missing-bundle")]
    [InlineData("protected-corruption")]
    public async Task Independently_altered_retained_state_is_rejected_without_live_reads(string damage)
    {
        await using var f=await Fixture.Create(); var bundle=await f.Capture(); var reference=await f.Retained.RetainAsync(bundle);
        var member=bundle.Manifest.Members[0];
        switch(damage)
        {
            case "missing-member": bundle.Content.Clear(); break;
            case "missing-manifest": bundle=bundle with { Manifest=null! }; break;
            case "truncated": bundle.Content[f.Artifact.Value]=f.Bytes[..^1]; break;
            case "bytes": bundle.Content[f.Artifact.Value][0]^=1; break;
            case "hash": bundle=bundle with { Manifest=bundle.Manifest with { Members=[member with { Sha256=new string('0',64) }] } }; break;
            case "length": bundle=bundle with { Manifest=bundle.Manifest with { Members=[member with { Length=member.Length+1 }] } }; break;
            case "snapshot": bundle=bundle with { Manifest=bundle.Manifest with { SnapshotId=OperationSnapshotId.New() } }; break;
            case "revision": bundle=bundle with { Manifest=bundle.Manifest with { Members=[member with { PhysicalRevision="different-revision" }] } }; break;
            case "version": bundle=bundle with { Manifest=bundle.Manifest with { Version=2 } }; break;
            case "reference": reference=reference with { SnapshotId=OperationSnapshotId.New() }; break;
            case "missing-bundle": File.Delete(Directory.GetFiles(Path.Combine(f.Root,"retained"),"*.candidate").Single()); break;
            case "protected-corruption": await File.WriteAllTextAsync(Directory.GetFiles(Path.Combine(f.Root,"retained"),"*.candidate").Single(),"corrupt"); break;
        }
        if(damage is not ("reference" or "missing-bundle" or "protected-corruption")) await ReplaceRetained(f,bundle);
        // The retained reader has only staging and protection dependencies. Destroy
        // the live metadata DB and source directory to make fallback impossible.
        File.Delete(f.Database); Directory.Delete(f.SourceRoot,true);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Retained.ReadValidatedAsync(reference));
    }
    [Theory]
    [InlineData("authority")]
    [InlineData("fingerprint")]
    [InlineData("provenance")]
    [InlineData("relationships")]
    [InlineData("absences")]
    public async Task Internally_inconsistent_metadata_is_rejected_before_retention(string damage)
    {
        await using var f=await Fixture.Create(); var bundle=await f.Capture();
        var metadata=bundle.Manifest.Metadata[0];
        var parts=JsonSerializer.Deserialize<Dictionary<string,string>>(metadata.Representation)!;
        switch(damage)
        {
            case "authority": parts["Authority"]=parts["Authority"].Replace("classification-1","classification-other"); break;
            case "fingerprint": parts["Artifact"]=parts["Artifact"].Replace(bundle.Manifest.Members[0].Fingerprint,new string('0',64)); break;
            case "provenance": parts["Provenance"]=parts["Provenance"].Replace("synthetic","other"); break;
            case "relationships": parts["Relationships"]="[{\"SourceArtifactId\":\"synthetic\",\"TargetArtifactId\":\"outside\"}]"; break;
            case "absences": bundle=bundle with { Manifest=bundle.Manifest with { ExplicitAbsences=[] } }; break;
        }
        var representation=JsonSerializer.Serialize(parts);
        bundle=bundle with { Manifest=bundle.Manifest with { Metadata=[metadata with
        { Representation=representation,Sha256=ReviewerRetainedValidator.Hash(Encoding.UTF8.GetBytes(representation)) }] } };
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Retained.RetainAsync(bundle));
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Root,"retained"),"*.candidate"));
    }

    [Fact]
    public async Task Retained_copy_survives_source_replacement_deletion_and_generation_gc_without_live_dependencies()
    {
        await using var f=await Fixture.Create(); var bundle=await f.Capture(); var reference=await f.Retained.RetainAsync(bundle);
        await f.Secured.WriteAsync(f.Artifact,Encoding.UTF8.GetBytes("later source")); await f.Secured.DeleteAsync(f.Artifact);
        using var gc=new FileSystemArtifactContentGarbageCollector(f.SourceRoot);
        Assert.True((await gc.CollectAsync()).FilesReclaimed>0);
        File.Delete(f.Database); Directory.Delete(f.SourceRoot,true);
        var retained=await f.Retained.ReadValidatedAsync(reference);
        Assert.Equal(f.Bytes,retained.Content[f.Artifact.Value]);
        Assert.Equal(bundle.Manifest.Members[0].PhysicalRevision,retained.Manifest.Members[0].PhysicalRevision);
    }
    [Fact]
    public async Task Source_and_integrity_failure_dispose_capture_and_leave_independent_operation_usable()
    {
        await using var f=await Fixture.Create();
        await using(var s=await f.Open(source:new SourceProxy(f.Secured,_=>throw new IOException("synthetic source failure"))))
            await Assert.ThrowsAsync<IOException>(()=>s.CaptureAsync());
        await f.Sql("UPDATE Artifacts SET FingerprintValue='wrong'");
        await using(var s=await f.Open())
        { var captured=await s.CaptureAsync(); await Assert.ThrowsAsync<InvalidDataException>(()=>f.Retained.RetainAsync(captured)); }
        await f.Sql("UPDATE Artifacts SET FingerprintValue=$hash",ReviewerRetainedValidator.Hash(f.Bytes)); await f.AssertUsable();
    }
    [Fact]
    public async Task Orchestration_closes_live_session_before_protected_retention()
    {
        await using var f=await Fixture.Create(); var session=await f.Open();
        var checkedDisposed=false;
        var encryption=new ProtectionProxy(f.Encryption,async ct=>{
            await Assert.ThrowsAsync<ObjectDisposedException>(()=>session.CaptureAsync(ct));
            await f.Sql("UPDATE Artifacts SET Name=Name"); checkedDisposed=true;
        },null);
        var store=new ProtectedReviewerRetainedMaterialStore(f.Staging,encryption);
        var reference=await store.CaptureAndRetainAsync(session);
        Assert.True(checkedDisposed); Assert.NotNull(await store.ReadValidatedAsync(reference));
    }
    [Fact]
    public async Task Idle_session_deadline_closes_read_view_without_caller_disposal()
    {
        await using var f=await Fixture.Create();
        var detached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session=await f.Open(new(TimeSpan.FromMilliseconds(200)));
        session.SqliteCheckpoint=(phase,_)=> { if(phase=="Detached") detached.TrySetResult(); };
        await detached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>session.CaptureAsync());
        await session.DisposeAsync();
        await f.AssertUsable();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Orchestration_clears_owned_plaintext_after_retention_success_or_failure(bool fail)
    {
        await using var f = await Fixture.Create();
        var bundle = await f.Capture();
        var capture = new CapturedSession(bundle);
        var encryption = new ProtectionProxy(f.Encryption, _ =>
            fail ? Task.FromException(new IOException("synthetic retention failure")) : Task.CompletedTask, null);
        var store = new ProtectedReviewerRetainedMaterialStore(f.Staging, encryption);
        if (fail)
            await Assert.ThrowsAsync<IOException>(() => store.CaptureAndRetainAsync(capture));
        else
        {
            var reference = await store.CaptureAndRetainAsync(capture);
            Assert.Equal(f.Bytes, (await store.ReadValidatedAsync(reference)).Content[f.Artifact.Value]);
        }
        Assert.True(capture.Disposed);
        Assert.All(bundle.Content.Values, bytes => Assert.All(bytes, value => Assert.Equal((byte)0, value)));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("allocated")]
    [InlineData("copy")]
    [InlineData("integrity")]
    [InlineData("partial")]
    [InlineData("protection")]
    [InlineData("staging")]
    [InlineData("validation")]
    [InlineData("cancellation")]
    [InlineData("disposal")]
    public async Task Owned_capture_buffers_are_cleared_without_mutating_provider_memory_or_evidence(string phase)
    {
        await using var f = await Fixture.Create();
        if (phase == "integrity") await f.Sql("UPDATE Artifacts SET FingerprintValue='wrong'");
        var providerBytes = f.Bytes.ToArray();
        var revision = new ArtifactContentRevision("provider-revision");
        var provider = new SourceProxy(f.Secured, _ => Task.CompletedTask)
        { ReturnedSnapshot = new(providerBytes, revision) };
        var requested = phase == "partial" ? new[] { f.Artifact, new ArtifactId("zzz-missing") } : new[] { f.Artifact };
        var session = await ReviewerCaptureSession.OpenAsync(f.Database, f.Snapshot, requested,
            provider, new(TimeSpan.FromSeconds(10)));
        var owned = new List<byte[]>();
        session.OwnedPlaintextCheckpoint = (point, bytes) =>
        {
            if (point == "Allocated") owned.Add(bytes);
            if (phase == "allocated" && point == "Allocated") throw new IOException("synthetic allocation checkpoint failure");
            if (phase == "copy" && point == "Copied") throw new IOException("synthetic copy failure");
        };
        if (phase == "disposal")
            session.SqliteCheckpoint = (point, _) => { if (point == "Detached") throw new IOException("synthetic disposal failure"); };
        using var cts = new CancellationTokenSource();
        var encryption = new ProtectionProxy(f.Encryption, ct =>
        {
            if (phase == "protection") throw new IOException("synthetic protection failure");
            if (phase == "cancellation") { cts.Cancel(); ct.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        }, phase == "validation" ? _ => throw new IOException("synthetic validation failure") : null);
        var staging = new StagingProxy(f.Staging, phase == "staging" ? _ => throw new IOException("synthetic staging failure") : null);
        var store = new ProtectedReviewerRetainedMaterialStore(staging, encryption);
        if (phase == "success")
        {
            var reference = await store.CaptureAndRetainAsync(session, cts.Token);
            var retained = await store.ReadValidatedAsync(reference);
            Assert.Equal(f.Bytes, retained.Content[f.Artifact.Value]);
            Assert.Equal(revision.Value, retained.Manifest.Members[0].PhysicalRevision);
            Assert.Equal(ReviewerRetainedValidator.Hash(f.Bytes), retained.Manifest.Members[0].Sha256);
        }
        else if (phase == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CaptureAndRetainAsync(session, cts.Token));
        else if (phase is "partial" or "integrity")
            await Assert.ThrowsAsync<InvalidDataException>(() => store.CaptureAndRetainAsync(session, cts.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => store.CaptureAndRetainAsync(session, cts.Token));
        Assert.Single(owned);
        Assert.NotSame(providerBytes, owned[0]);
        Assert.All(owned[0], value => Assert.Equal((byte)0, value));
        Assert.Equal(f.Bytes, providerBytes);
        if (phase is not ("allocated" or "copy" or "partial" or "integrity" or "disposal"))
        {
            Assert.False(encryption.LastEncryptPlaintext.IsEmpty);
            Assert.All(encryption.LastEncryptPlaintext.ToArray(), value => Assert.Equal((byte)0, value));
        }
        // Cleanup failure remains observable on subsequent disposal joins.
        if (phase == "disposal")
            await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());
        else
            await session.DisposeAsync();
    }

    [Fact]
    public async Task Direct_retention_preserves_caller_owned_bundle_and_read_returns_independent_plaintext()
    {
        await using var f = await Fixture.Create();
        var bundle = await f.Capture();
        var member = bundle.Manifest.Members[0];
        var reference = await f.Retained.RetainAsync(bundle);
        Assert.Equal(f.Bytes, bundle.Content[f.Artifact.Value]);
        var retained = await f.Retained.ReadValidatedAsync(reference);
        Assert.NotSame(bundle.Content[f.Artifact.Value], retained.Content[f.Artifact.Value]);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bundle.Content[f.Artifact.Value]);
        Assert.Equal(f.Bytes, retained.Content[f.Artifact.Value]);
        Assert.Equal(member, retained.Manifest.Members[0]);
    }

    private sealed class CapturedSession(ReviewerCapturedBundle bundle)
        : EMF.Extensions.VeteransClaims.Contracts.IReviewerCaptureSession
    {
        public bool Disposed { get; private set; }
        public Task<ReviewerCapturedBundle> CaptureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(bundle);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task Deadline_interrupts_active_SQLite_work()
        => await InterruptActiveSqlite(callerCancellation:false);

    [Fact]
    public async Task Caller_cancellation_interrupts_active_SQLite_work()
        => await InterruptActiveSqlite(callerCancellation:true);

    [Fact]
    public async Task Progress_callback_is_detached_after_SQLite_interruption()
        => Assert.True((await InterruptActiveSqlite(callerCancellation:true)).CallbackDetached);

    [Fact]
    public async Task Interrupted_connection_has_no_stale_interrupt_on_next_operation()
        => Assert.Equal(SQLitePCL.raw.SQLITE_OK,(await InterruptActiveSqlite(callerCancellation:true)).NextResult);

    [Fact]
    public async Task Interrupt_does_not_affect_independent_connection()
        => Assert.Equal(42L,(await InterruptActiveSqlite(callerCancellation:true)).IndependentResult);

    private static async Task<(bool CallbackDetached,int NextResult,long IndependentResult)> InterruptActiveSqlite(bool callerCancellation)
    {
        await using var f=await Fixture.Create();
        await f.Sql("WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<20000) INSERT INTO Relationships SELECT x,'other','other' FROM n");
        using var cts=new CancellationTokenSource();
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expired=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken deadlineToken=default;
        using var release=new ManualResetEventSlim();
        var detached=false; var detachedQueryResult=-1; var callbacks=0;
        await using var session=await f.Open(new(TimeSpan.FromSeconds(callerCancellation?10:1),MaximumWork:2000000),
            checkpoint:(phase,ct)=> { if(phase=="Acquiring") deadlineToken=ct; return Task.CompletedTask; });
        using var deadlineSignal=deadlineToken.Register(()=>expired.TrySetResult());
        session.SqliteCheckpoint=(phase,c)=>
        {
            if(phase=="Progress")
            {
                callbacks++; reached.TrySetResult();
                if(!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("SQLite barrier was not released.");
            }
            else
            {
                var before=callbacks;
                detachedQueryResult=SQLitePCL.raw.sqlite3_exec(c.Handle!,"SELECT SUM(Id) FROM Relationships");
                detached=before==callbacks;
            }
        };
        // The independent connection is open before cancellation and remains usable afterward.
        await using var independent=new SqliteConnection($"Data Source={f.Database};Pooling=False");
        await independent.OpenAsync();
        var task=Task.Run(()=>session.CaptureAsync(cts.Token));
        long independentResult=0;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await f.Sql("UPDATE Artifacts SET Name='independent'");
            if(callerCancellation) cts.Cancel();
            else await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await using var command=independent.CreateCommand(); command.CommandText="SELECT 42";
            independentResult=(long)(await command.ExecuteScalarAsync())!;
        }
        finally { release.Set(); }
        var error=await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsType<SqliteException>(error.InnerException); // Proves interruption came from active native SQL.
        Assert.True(callbacks>0);
        cts.Cancel(); await f.Sql("DELETE FROM Relationships"); await f.AssertUsable();
        return (detached,detachedQueryResult,independentResult);
    }
    [Fact]
    public async Task Progress_callback_is_detached_after_normal_completion_and_next_operation_is_unaffected()
    {
        await using var f=await Fixture.Create();
        await f.Sql("WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<20000) INSERT INTO Relationships SELECT x,'other','other' FROM n");
        await using var session=await f.Open(new(TimeSpan.FromSeconds(10),MaximumWork:2000000));
        var callbacks=0; var detached=false;
        session.SqliteCheckpoint=(phase,c)=>
        {
            if(phase=="Progress") callbacks++;
            else
            {
                var before=callbacks;
                Assert.Equal(SQLitePCL.raw.SQLITE_OK,SQLitePCL.raw.sqlite3_exec(c.Handle!,"SELECT SUM(Id) FROM Relationships"));
                Assert.Equal(before,callbacks); detached=true;
            }
        };
        await session.CaptureAsync(); Assert.True(callbacks>0); Assert.True(detached);
        await f.Sql("DELETE FROM Relationships"); await f.AssertUsable();
    }
    [Fact]
    public async Task Non_cancellation_SQLite_failure_preserves_original_error_code_and_releases_resources()
    {
        await using var f=await Fixture.Create(); await using var session=await f.Open();
        // Corrupt fixture shape while preserving the first-read table; capture
        // still owns the established view, so use a fresh session for missing table.
        await session.DisposeAsync(); await f.Sql("ALTER TABLE Provenance RENAME TO SavedProvenance");
        await using var broken=await f.Open();
        var error=await Assert.ThrowsAsync<SqliteException>(()=>broken.CaptureAsync());
        Assert.Equal(SQLitePCL.raw.SQLITE_ERROR,error.SqliteErrorCode); Assert.Contains("no such table: Provenance",error.Message);
        await f.Sql("ALTER TABLE SavedProvenance RENAME TO Provenance"); await f.AssertUsable();
    }
    private static async Task ReplaceRetained(Fixture f,ReviewerCapturedBundle bundle)
    {
        var context=Encoding.UTF8.GetBytes("EMF-REVIEWER-CAPTURE-v1\0"+f.Snapshot.Value);
        var envelope=await f.Encryption.EncryptWithContextAsync(ReviewerRetainedValidator.Encode(bundle),context);
        await File.WriteAllBytesAsync(Directory.GetFiles(Path.Combine(f.Root,"retained"),"*.candidate").Single(),JsonSerializer.SerializeToUtf8Bytes(envelope));
    }
    private sealed class ProtectionProxy(IEnvelopeEncryptionService inner,Func<CancellationToken,Task>? encrypt,Func<CancellationToken,Task>? decrypt):IEnvelopeEncryptionService
    {
        public ReadOnlyMemory<byte> LastEncryptPlaintext { get; private set; }
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> bytes,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<byte[]> DecryptAsync(EncryptedEnvelope envelope,CancellationToken ct=default)=>throw new NotSupportedException();
        public async Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> bytes,ReadOnlyMemory<byte> context,CancellationToken ct=default)
        { LastEncryptPlaintext = bytes; if(encrypt is not null) await encrypt(ct); return await inner.EncryptWithContextAsync(bytes,context,ct); }
        public async Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope envelope,ReadOnlyMemory<byte> context,CancellationToken ct=default)
        { if(decrypt is not null) await decrypt(ct); return await inner.DecryptWithContextAsync(envelope,context,ct); }
    }
    private sealed class StagingProxy(IArtifactContentStagingStore inner,Func<CancellationToken,Task>? stage):IArtifactContentStagingStore
    {
        public async Task StageAsync(ArtifactContentOperationId id,ReadOnlyMemory<byte> bytes,CancellationToken ct=default)
        { if(stage is not null) await stage(ct); await inner.StageAsync(id,bytes,ct); }
        public Task<byte[]?> ReadAsync(ArtifactContentOperationId id,CancellationToken ct=default)=>inner.ReadAsync(id,ct);
    }
    private sealed class SourceProxy(IVersionedArtifactContentStore inner,Func<CancellationToken,Task> read):IVersionedArtifactContentStore
    {
        public ArtifactContentSnapshot? ReturnedSnapshot { get; init; }
        public async Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id,CancellationToken ct=default)
        { await read(ct); return ReturnedSnapshot ?? await inner.ReadVersionedAsync(id,ct); }
        public Task WriteAsync(ArtifactId id,ReadOnlyMemory<byte> bytes,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task DeleteAsync(ArtifactId id,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<byte[]?> ReadAsync(ArtifactId id,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id,ArtifactContentRevision revision,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id,ArtifactContentRevision revision,ArtifactContentMutationContext context,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId id,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(ArtifactContentReceiptCursor? cursor,int limit,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
