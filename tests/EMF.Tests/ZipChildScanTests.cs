using System.IO.Compression;
using System.Security.Cryptography;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Malware;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;
public sealed class ZipChildScanTests
{
    private sealed class Crash:Exception;
    internal sealed class Encryption:IEnvelopeEncryptionService,IBoundedEnvelopeDecryptionService
    {
        private readonly DevelopmentEnvelopeEncryptionService _inner=new(new ArtifactIngestionFixture.Keys());internal byte[]? Plaintext;
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> p,CancellationToken ct=default)=>_inner.EncryptAsync(p,ct);
        public Task<byte[]> DecryptAsync(EncryptedEnvelope e,CancellationToken ct=default)=>_inner.DecryptAsync(e,ct);
        public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> p,ReadOnlyMemory<byte> a,CancellationToken ct=default)=>_inner.EncryptWithContextAsync(p,a,ct);
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope e,ReadOnlyMemory<byte> a,CancellationToken ct=default)=>_inner.DecryptWithContextAsync(e,a,ct);
        public async Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope e,ReadOnlyMemory<byte> a,EnvelopeDecryptionLimits l,CancellationToken ct=default)
        {Plaintext=await _inner.DecryptWithContextBoundedAsync(e,a,l,ct);return Plaintext;}
    }
    internal sealed class Scanner:IMalwareScanner
    {
        public MalwareScannerPolicy Policy{get;set;}=new("fixture","1",new string('C',64));internal int Calls;
        internal MalwareDetection Detection=MalwareDetection.NoThreatDetected;internal MalwareCoverage Coverage=MalwareCoverage.Complete;
        internal MalwareScanFailure Failure=MalwareScanFailure.None;internal bool WrongBinding,MissingProof;internal Func<Task>? During;
        public async Task<MalwareScanEvidence> ScanAsync(MalwareScanRequest r,Stream source,CancellationToken ct=default)
        {
            Calls++;Assert.Equal(typeof(MemoryStream),source.GetType());Assert.Equal(r.Content.Length,source.Length);
            if(During is not null)await During();var buffer=new byte[65536];using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long read=0;
            try{while(read<r.Content.Length){var n=checked((int)Math.Min(buffer.Length,r.Content.Length-read));await source.ReadExactlyAsync(buffer.AsMemory(0,n),ct);hash.AppendData(buffer,0,n);read+=n;}}
            finally{CryptographicOperations.ZeroMemory(buffer);}
            var observed=Convert.ToHexString(hash.GetHashAndReset());
            return new(WrongBinding?r with{Content=r.Content with{Sha256=new string('D',64)}}:r,Detection,Coverage,Failure,read,read,true,observed,"fixture-engine","fixture-db",DateTimeOffset.UtcNow,MissingProof?null:"certified-fixture-completeness");
        }
    }
    internal sealed class Fixture:IAsyncDisposable
    {
        internal ArtifactIngestionFixture Evidence=null!;internal Encryption Encryption=new();internal ArtifactContentReadLease Lease=null!;internal ZipParentSnapshot Parent=null!;
        internal SqliteZipExtractionJournal Journal=>new(Evidence.DatabasePath);
        internal ZipPrivateContentStorage Storage=>new(Path.Combine(Evidence.Root,"private-zip"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
        internal ZipProtectedRetentionService Retention()=>new(Journal,Journal,Storage,Encryption);
        internal ZipChildScanService Service(IMalwareScanner scanner)=>new(Journal,Journal,Journal,Retention(),scanner);
        internal static async Task<Fixture> Create(string name="child.bin",byte[]? child=null,bool materialize=true)
        {
            child??="nonzero child payload"u8.ToArray();using var stream=new MemoryStream();using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true)){using var entry=archive.CreateEntry(name).Open();entry.Write(child);}
            var bytes=stream.ToArray();var f=new Fixture{Evidence=await ArtifactIngestionFixture.CreateAsync()};await f.Journal.InitializeAsync();
            var binding=new ZipParentBinding("zip","parent",new("input","revision",Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length),new string('B',64));await f.Journal.CreateAsync(binding);
            f.Parent=await f.Journal.ClaimAsync("zip","owner",TimeSpan.FromMinutes(5));f.Lease=new(new("input"),new("revision"),bytes.Length,bytes);
            f.Parent=await new ZipEntryPlanner().AdmitAsync(f.Journal,f.Parent,f.Lease);if(materialize)f.Parent=await new ZipSequentialExtractor(f.Journal,f.Journal,f.Journal,f.Retention()).MaterializeNextAsync(f.Parent,f.Lease);return f;
        }
        internal async Task Refresh()=>Parent=(await Journal.ReadAsync("zip"))!;
        internal async Task Sql(string sql){await using var c=new SqliteConnection("Data Source="+Evidence.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText=sql;await q.ExecuteNonQueryAsync();}
        public async ValueTask DisposeAsync(){Lease.Dispose();await Evidence.DisposeAsync();}
    }
    [Fact]
    public async Task Trusted_complete_provider_records_bound_clean_without_publication_and_reentry_spends_no_new_attempt()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner();f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);
        Assert.Equal(ZipEntryState.Scanned,f.Parent.Entries[0].State);Assert.Equal(1,f.Parent.Budget.ScannerAttempts);Assert.Equal(f.Parent.Entries[0].Plan.ExpandedLength,f.Parent.Budget.ScannerReserved);
        var evidence=System.Text.Json.JsonSerializer.Deserialize<MalwareScanEvidence>(f.Parent.Entries[0].EvidenceJson!)!;Assert.True(evidence.IsClean);Assert.Equal(f.Parent.Entries[0].Retained!.Sha256,evidence.Request.Content.Sha256);
        Assert.True(f.Encryption.Plaintext!.All(b=>b==0));Assert.Null(await f.Evidence.Repository.GetArtifactAsync(new(f.Parent.Entries[0].Plan.ProvisionalArtifactId!)));
        var before=f.Parent.Budget;f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(1,scanner.Calls);Assert.Equal(before,f.Parent.Budget);
    }
    [Theory]
    [InlineData(MalwareDetection.NoThreatDetected,MalwareCoverage.Unknown,MalwareScanFailure.CoverageUnproven)]
    [InlineData(MalwareDetection.NoThreatDetected,MalwareCoverage.Partial,MalwareScanFailure.CoverageUnproven)]
    [InlineData(MalwareDetection.MalwareDetected,MalwareCoverage.Unknown,MalwareScanFailure.Threat)]
    [InlineData(MalwareDetection.Error,MalwareCoverage.Partial,MalwareScanFailure.LimitExceeded)]
    public async Task Nonclean_outcomes_require_review_and_never_admit_ingestion(MalwareDetection detection,MalwareCoverage coverage,MalwareScanFailure failure)
    {
        await using var f=await Fixture.Create();var scanner=new Scanner{Detection=detection,Coverage=coverage,Failure=failure};f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);
        Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(ZipEntryState.RequiresReview,f.Parent.Entries[0].State);Assert.Equal(1,f.Parent.Budget.RejectedEntries);
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.ReserveAsync(f.Parent.Fence,new("adopt",ZipWorkKind.Ingestion,0)));
        Assert.True(f.Encryption.Plaintext!.All(b=>b==0));Assert.Equal(ZipRetentionState.Created,(await f.Journal.ReadRetentionAsync("zip",0))!.State);
    }
    [Theory]
    [InlineData("ordinary.txt")] [InlineData("nested.zip")]
    public async Task Actual_stock_clamd_OK_remains_unknown_for_text_and_nested_zip(string name)
    {
        byte[] child;
        if(name=="nested.zip"){using var bytes=new MemoryStream();using(var nested=new ZipArchive(bytes,ZipArchiveMode.Create,true)){using var inner=nested.CreateEntry("inner.txt").Open();inner.Write("nested payload"u8);}child=bytes.ToArray();}
        else child="ordinary text payload"u8.ToArray();
        await using var f=await Fixture.Create(name,child);var scanner=new ClamdMalwareScanner(ClamdMalwareScannerTests.Replies("stream: OK\0"),ClamdMalwareScannerTests.Profile());
        f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);var evidence=System.Text.Json.JsonSerializer.Deserialize<MalwareScanEvidence>(f.Parent.Entries[0].EvidenceJson!)!;
        Assert.Equal(MalwareDetection.NoThreatDetected,evidence.Detection);Assert.Equal(MalwareCoverage.Unknown,evidence.Coverage);Assert.False(evidence.IsClean);Assert.Equal(1,f.Parent.Budget.ExtractionAttempts);Assert.Equal(1,f.Parent.Budget.ScannerAttempts);
    }
    [Fact]
    public async Task Transport_failure_is_durable_and_second_submission_is_charged_before_retry()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner{Detection=MalwareDetection.Error,Coverage=MalwareCoverage.Unknown,Failure=MalwareScanFailure.TransportError};
        f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[0].State);
        scanner.Detection=MalwareDetection.NoThreatDetected;scanner.Coverage=MalwareCoverage.Complete;scanner.Failure=MalwareScanFailure.None;
        f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(ZipEntryState.Scanned,f.Parent.Entries[0].State);Assert.Equal(2,f.Parent.Budget.ScannerAttempts);
        Assert.Equal(2*f.Parent.Entries[0].Plan.ExpandedLength,f.Parent.Budget.ScannerReserved);Assert.Equal(f.Parent.Entries[0].Plan.ExpandedLength,f.Parent.Budget.ReplayScanner);
    }
    [Fact]
    public async Task Two_transport_errors_require_review_and_a_third_external_submission_never_starts()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner{Detection=MalwareDetection.Error,Coverage=MalwareCoverage.Unknown,Failure=MalwareScanFailure.TransportError};
        for(var i=0;i<2;i++)f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service(scanner).ScanNextAsync(f.Parent));Assert.Equal(2,scanner.Calls);Assert.Equal(2,f.Parent.Budget.ScannerAttempts);
    }
    [Theory]
    [InlineData("Reserved")] [InlineData("ScannerReturned")] [InlineData("PlaintextCleared")]
    public async Task Interrupted_scan_has_no_clean_handoff_and_fresh_runtime_charges_a_new_attempt(string boundary)
    {
        await using var f=await Fixture.Create();var scanner=new Scanner();var service=f.Service(scanner);service.Checkpoint=n=>n==boundary?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.ScanNextAsync(f.Parent));await f.Refresh();Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[0].State);Assert.Equal(1,f.Parent.Budget.ScannerAttempts);
        f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(2,f.Parent.Budget.ScannerAttempts);Assert.Equal(ZipEntryState.Scanned,f.Parent.Entries[0].State);
    }
    [Fact]
    public async Task Completed_scan_crash_recovers_without_duplicate_submission_or_reservation()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner();var service=f.Service(scanner);service.Checkpoint=n=>n=="Completed"?throw new Crash():Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(()=>service.ScanNextAsync(f.Parent));await f.Refresh();f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(1,scanner.Calls);Assert.Equal(1,f.Parent.Budget.ScannerAttempts);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Wrong_fingerprint_or_missing_completeness_proof_fails_closed(bool missingProof)
    {
        await using var f=await Fixture.Create();var scanner=new Scanner{MissingProof=missingProof,WrongBinding=!missingProof};
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service(scanner).ScanNextAsync(f.Parent));await f.Refresh();Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);Assert.Equal(1,f.Parent.Budget.ScannerAttempts);Assert.True(f.Encryption.Plaintext!.All(b=>b==0));
    }
    [Fact]
    public async Task Cancellation_clears_owned_plaintext_and_preserves_nonrefundable_reserved_attempt()
    {
        await using var f=await Fixture.Create();using var ct=new CancellationTokenSource();var scanner=new Scanner{During=()=>{ct.Cancel();return Task.CompletedTask;}};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Service(scanner).ScanNextAsync(f.Parent,ct.Token));await f.Refresh();Assert.Equal(1,f.Parent.Budget.ScannerAttempts);Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[0].State);Assert.True(f.Encryption.Plaintext!.All(b=>b==0));
        scanner.During=null;f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);Assert.Equal(2,f.Parent.Budget.ScannerAttempts);
    }
    [Fact]
    public async Task Scanner_io_holds_no_sqlite_write_transaction_and_stale_completion_is_rejected()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner();scanner.During=async()=>{await f.Sql("BEGIN IMMEDIATE; ROLLBACK;");await f.Journal.ClaimAsync("zip","owner",TimeSpan.FromMinutes(5));};
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Service(scanner).ScanNextAsync(f.Parent));await f.Refresh();Assert.Equal(ZipEntryState.Materialized,f.Parent.Entries[0].State);Assert.Equal(1,f.Parent.Budget.ScannerAttempts);Assert.True(f.Encryption.Plaintext!.All(b=>b==0));
    }
    [Fact]
    public async Task Scanner_policy_cannot_change_after_first_durable_attempt()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner{Detection=MalwareDetection.Error,Coverage=MalwareCoverage.Unknown,Failure=MalwareScanFailure.TransportError};f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);
        scanner.Policy=scanner.Policy with{Hash=new string('D',64)};await Assert.ThrowsAsync<InvalidDataException>(()=>f.Service(scanner).ScanNextAsync(f.Parent));await f.Refresh();Assert.Equal(1,f.Parent.Budget.ScannerAttempts);Assert.Equal(1,scanner.Calls);Assert.Equal(ZipParentState.RequiresReview,f.Parent.State);
    }
    [Fact]
    public async Task Immutable_scan_evidence_cannot_be_deleted_replaced_or_relabelled_clean()
    {
        await using var f=await Fixture.Create();var scanner=new Scanner{Coverage=MalwareCoverage.Unknown,Failure=MalwareScanFailure.CoverageUnproven};f.Parent=await f.Service(scanner).ScanNextAsync(f.Parent);
        foreach(var sql in new[]{"DELETE FROM ZipExtractionScans","INSERT OR REPLACE INTO ZipExtractionScans SELECT * FROM ZipExtractionScans","UPDATE ZipExtractionScans SET BindingJson='{}'","UPDATE ZipExtractionScans SET EvidenceJson='{}'"})await Assert.ThrowsAsync<SqliteException>(()=>f.Sql(sql));
        await f.Sql("UPDATE ZipExtractionEntries SET ProgressJson=json_set(ProgressJson,'$.State',2);UPDATE ZipExtractionParents SET State=2,Revision=Revision+1,BudgetJson=json_set(BudgetJson,'$.RejectedEntries',0)");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Journal.ReadAsync("zip"));
    }
    [Fact]
    public async Task Cross_stage_gate_clears_scan_plaintext_before_another_extractor_allocates()
    {
        await using var f=await Fixture.Create();await using var other=await Fixture.Create(materialize:false);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner=new Scanner{During=async()=>{entered.SetResult();await release.Task;}};var scan=f.Service(scanner).ScanNextAsync(f.Parent);await entered.Task;
        var nextStarted=false;var extractor=new ZipSequentialExtractor(other.Journal,other.Journal,other.Journal,other.Retention());extractor.Allocated=_=>{nextStarted=true;Assert.True(f.Encryption.Plaintext!.All(b=>b==0));};
        var extraction=extractor.MaterializeNextAsync(other.Parent,other.Lease);Assert.False(nextStarted);release.SetResult();await Task.WhenAll(scan,extraction);Assert.True(nextStarted);
    }
}
