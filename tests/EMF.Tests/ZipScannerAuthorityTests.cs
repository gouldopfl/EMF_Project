using System.Text.Json;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
using Xunit;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipScannerAuthorityTests
{
    internal sealed class Setup: IAsyncDisposable
    {
        internal ZipOwnershipFixture Fixture=null!;
        internal SyntheticZipExecutionGrant Root=null!;
        internal ZipExecutionAttempt Attempt=null!;
        internal SyntheticGuardedZipScanner Provider=null!;
        internal static async Task<Setup> Create(TimeSpan? rootLife=null)
        {
            var s=new Setup{Fixture=await ZipOwnershipFixture.CreateAsync()};var f=s.Fixture;
            s.Provider=new(f.Profile.ScannerPolicy);
            s.Provider.Submitted=()=>f.Runtime.Log("scan");
            f.Runtime.Scan=new(f.Journal,f.Journal,f.Journal,f.Runtime.ChildRetention,s.Provider);f.Runtime.ComposeDriver();
            s.Root=new(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock,rootLife);
            s.Attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,f.Clock),s.Root,await f.RequestAsync(s.Root.Purpose),f.Clock);
            Assert.Equal(ZipPhaseIIStop.AwaitingScanner,(await f.Runtime.Driver.RunAsync(s.Attempt)).Stop);return s;
        }
        internal Task<ZipScannerResult> Scan(CancellationToken ct=default)=>Fixture.Runtime.Driver.RunScannerAsync(Attempt,ct);
        internal Task<ZipParentSnapshot?> Read()=>Fixture.Journal.ReadAsync(Fixture.Admission.OperationId);
        internal async Task<ZipScannerResult> Recover()
        {
            var f=Fixture;await using var root=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.Recovery,f.Clock);
            await using var attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,f.Clock),root,await f.RequestAsync(root.Purpose),f.Clock);
            return await f.Runtime.Driver.RunScannerAsync(attempt);
        }
        public async ValueTask DisposeAsync(){await Attempt.DisposeAsync();await Root.DisposeAsync();await Fixture.DisposeAsync();}
    }
    internal static async Task Denied(Task task)
    {
        var error=await Record.ExceptionAsync(()=>task.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.True(error is UnauthorizedAccessException or OperationCanceledException,$"Actual denial: {error}");
    }
    internal static void NoDownstream(ZipOwnershipFixture f,ZipParentSnapshot p)
    {
        Assert.Equal(-1,p.Fence.ConfirmedOrdinal);Assert.Equal(0,p.Budget.IngestionAttempts);Assert.Equal(0,p.Budget.RejectedEntries);
        Assert.NotEqual(ZipParentState.RequiresReview,p.State);Assert.NotEqual(ZipParentState.Completed,p.State);Assert.NotEqual(ZipParentState.Released,p.State);
        Assert.All(p.Entries,e=>{Assert.Null(e.Ingestion);Assert.NotEqual(ZipEntryState.RequiresReview,e.State);Assert.NotEqual(ZipEntryState.Released,e.State);});
        Assert.Equal(0,f.Runtime.Count("ingestion-encrypt"));Assert.Equal(0,f.Runtime.Count("Ingesting"));Assert.Equal(0,f.Runtime.Count("Resuming"));
    }
    [Fact]
    public async Task Valid_scan_stops_before_ingestion_with_all_downstream_dependencies_present()
    {
        await using var s=await Setup.Create();var f=s.Fixture;var result=await s.Scan();
        Assert.Equal(ZipScannerStop.AwaitingChildIngestion,result.Stop);Assert.Equal(ZipEntryState.Scanned,result.Parent.Entries[0].State);
        Assert.Equal(1,result.Parent.Budget.ScannerAttempts);Assert.Equal(1,s.Provider.Calls["Payload"]);
        Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(f.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));
        NoDownstream(f,result.Parent);await AssertNoDurableDownstreamAsync(f,result.Parent);
        var before=result.Parent.Budget;var reads=f.Runtime.Count("child-decrypt");var scans=f.Runtime.Count("scan");
        var recovered=await s.Recover();Assert.Equal(ZipScannerStop.AwaitingChildIngestion,recovered.Stop);
        Assert.Equal(before,recovered.Parent.Budget);Assert.Equal(reads,f.Runtime.Count("child-decrypt"));Assert.Equal(scans,f.Runtime.Count("scan"));
    }
    [Theory]
    [InlineData("revoke")][InlineData("expire")][InlineData("dispose")][InlineData("caller")]
    public async Task Returned_observation_after_execution_invalidation_records_only_and_recovers_without_rescan(string kind)
    {
        await using var s=await Setup.Create(TimeSpan.FromSeconds(30));var f=s.Fixture;using var caller=new CancellationTokenSource();
        s.Provider.BeforeReply=async()=>
        {
            Assert.All(f.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));
            switch(kind){case "revoke":s.Root.Revoke();break;case "expire":f.Clock.Now=s.Root.ValidUntilUtc;break;
                case "dispose":await s.Root.DisposeAsync();break;default:caller.Cancel();break;}
        };
        await Denied(s.Scan(caller.Token));var p=(await s.Read())!;
        Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.NotNull(p.Entries[0].EvidenceJson);
        Assert.True(JsonSerializer.Deserialize<MalwareScanEvidence>(p.Entries[0].EvidenceJson!)!.IsClean);
        Assert.Equal(1,p.Budget.ScannerAttempts);NoDownstream(f,p);
        var budget=p.Budget;var reads=f.Runtime.Count("child-decrypt");var scans=f.Runtime.Count("scan");s.Provider.BeforeReply=null;
        var result=await s.Recover();Assert.Equal(ZipScannerStop.AwaitingChildIngestion,result.Stop);Assert.Equal(budget,result.Parent.Budget);
        Assert.Equal(reads,f.Runtime.Count("child-decrypt"));Assert.Equal(scans,f.Runtime.Count("scan"));
    }
    [Theory]
    [InlineData("expiry")][InlineData("revocation")][InlineData("disposal")]
    public async Task Independent_recorder_invalidation_denies_factual_write_without_refunding_reservation(string kind)
    {
        await using var s=await Setup.Create();s.Root.RecorderLifetime=TimeSpan.FromSeconds(30);
        s.Provider.Returned=()=>
        {if(kind=="expiry")s.Fixture.Clock.Now=s.Root.LastRecorder!.ValidUntilUtc;else if(kind=="revocation")s.Root.RevokeRecorder!();else s.Root.LastRecorder!.DisposeAsync().GetAwaiter().GetResult();};
        await Denied(s.Scan());var p=(await s.Read())!;Assert.Null(p.Entries[0].EvidenceJson);
        Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.Equal(1,p.Budget.ScannerAttempts);NoDownstream(s.Fixture,p);
    }
    [Fact]
    public async Task Missing_recorder_denies_before_child_read_or_provider_submission()
    {
        await using var s=await Setup.Create();s.Root.DenyRecorder=true;var reads=s.Fixture.Runtime.Count("child-decrypt");
        await Denied(s.Scan());var p=(await s.Read())!;Assert.Equal(1,p.Budget.ScannerAttempts);Assert.Empty(s.Provider.Calls);
        Assert.Equal(reads,s.Fixture.Runtime.Count("child-decrypt"));Assert.Null(p.Entries[0].EvidenceJson);
    }
    [Fact]
    public async Task Frozen_clean_does_not_grant_progression_when_scope_is_denied()
    {
        await using var s=await Setup.Create();s.Root.DenyScanProgression=true;await Denied(s.Scan());var p=(await s.Read())!;
        Assert.NotNull(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);NoDownstream(s.Fixture,p);
        Assert.Equal(ZipScannerStop.AwaitingChildIngestion,(await s.Recover()).Stop);
    }
    [Theory]
    [InlineData(ZipAdmissionEventKind.Revoked)][InlineData(ZipAdmissionEventKind.RequiresReview)]
    public async Task Durable_admission_stop_allows_exact_fact_but_denies_progression_even_with_valid_live_root(ZipAdmissionEventKind kind)
    {
        await using var s=await Setup.Create();var f=s.Fixture;
        f.Runtime.Scan.Checkpoint=async point=>
        {if(point=="ScannerReturned")await f.Journal.AppendAdmissionEventAsync(f.Admission,new("scan-stop",kind,"synthetic",f.Clock.Now,SafeReason:"Stop"));};
        await Denied(s.Scan());Assert.False(s.Root.Revoked.IsCancellationRequested);var p=(await s.Read())!;
        Assert.NotNull(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);NoDownstream(f,p);
    }
    [Theory]
    [InlineData(MalwareDetection.MalwareDetected,MalwareCoverage.Unknown,MalwareScanFailure.Threat)]
    [InlineData(MalwareDetection.NoThreatDetected,MalwareCoverage.Unknown,MalwareScanFailure.CoverageUnproven)]
    [InlineData(MalwareDetection.Error,MalwareCoverage.Unknown,MalwareScanFailure.ProtocolError)]
    [InlineData(MalwareDetection.Error,MalwareCoverage.Partial,MalwareScanFailure.LimitExceeded)]
    public async Task Terminal_observation_never_marks_review_and_vetoes_reservations_across_restart(MalwareDetection detection,MalwareCoverage coverage,MalwareScanFailure failure)
    {
        await using var s=await Setup.Create();s.Provider.Detection=detection;s.Provider.Coverage=coverage;s.Provider.Failure=failure;
        var result=await s.Scan();Assert.Equal(ZipScannerStop.AwaitingReviewAuthorization,result.Stop);Assert.Equal(ZipEntryState.Materialized,result.Parent.Entries[0].State);
        NoDownstream(s.Fixture,result.Parent);var scans=s.Fixture.Runtime.Count("scan");var budget=result.Parent.Budget;
        var recovered=await s.Recover();Assert.Equal(ZipScannerStop.AwaitingReviewAuthorization,recovered.Stop);
        Assert.Equal(scans,s.Fixture.Runtime.Count("scan"));Assert.Equal(budget,recovered.Parent.Budget);
        var journal=new EMF.Persistence.Repositories.SqliteZipExtractionJournal(s.Fixture.Evidence.DatabasePath,s.Fixture.Clock);
        await journal.InitializeAsync();var p=(await journal.ReadAsync(s.Fixture.Admission.OperationId))!;
        await Assert.ThrowsAsync<InvalidDataException>(()=>journal.BeginScanAsync(p.Fence,0,s.Provider.Policy,new ValidGuard()));
        Assert.Equal(p.Budget,(await journal.ReadAsync(p.Binding.OperationId))!.Budget);
    }
    [Theory]
    [InlineData(MalwareScanFailure.TransportError)][InlineData(MalwareScanFailure.TimedOut)][InlineData(MalwareScanFailure.Cancelled)]
    public async Task First_transient_failure_can_retry_only_under_fresh_recovery_and_second_is_terminal(MalwareScanFailure failure)
    {
        await using var s=await Setup.Create();s.Provider.Detection=MalwareDetection.Error;s.Provider.Coverage=MalwareCoverage.Unknown;s.Provider.Failure=failure;
        var first=await s.Scan();Assert.Equal(ZipScannerStop.ScannerRetryRequired,first.Stop);
        await Denied(s.Scan());var second=await s.Recover();Assert.Equal(ZipScannerStop.AwaitingReviewAuthorization,second.Stop);
        Assert.Equal(2,second.Parent.Budget.ScannerAttempts);Assert.True(second.Parent.Budget.ReplayScanner>0);NoDownstream(s.Fixture,second.Parent);
        var scans=s.Fixture.Runtime.Count("scan");Assert.Equal(ZipScannerStop.AwaitingReviewAuthorization,(await s.Recover()).Stop);
        Assert.Equal(scans,s.Fixture.Runtime.Count("scan"));
    }
    [Theory]
    [InlineData("Header")][InlineData("Payload")][InlineData("Probe")][InlineData("Terminator")][InlineData("FinalFlush")]
    public async Task Invalidation_before_next_concrete_call_denies_that_effect_but_records_admitted_local_failure(string boundary)
    {
        await using var s=await Setup.Create();s.Provider.BeforeCall=point=>{if(point==boundary)s.Root.Revoke();return Task.CompletedTask;};
        await Denied(s.Scan());Assert.Equal(0,s.Provider.Calls.GetValueOrDefault(boundary));var p=(await s.Read())!;
        Assert.NotNull(p.Entries[0].EvidenceJson);Assert.False(JsonSerializer.Deserialize<MalwareScanEvidence>(p.Entries[0].EvidenceJson!)!.IsClean);
        Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);NoDownstream(s.Fixture,p);Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));
    }
    [Fact]
    public async Task Revocation_during_response_disposal_preserves_exact_frozen_observation_but_denies_handoff()
    {
        await using var s=await Setup.Create();s.Provider.BeforeDispose=()=>{s.Root.Revoke();return Task.CompletedTask;};
        await Denied(s.Scan());var p=(await s.Read())!;Assert.NotNull(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);
        NoDownstream(s.Fixture,p);Assert.Equal(ZipScannerStop.AwaitingChildIngestion,(await s.Recover()).Stop);
    }
    [Fact]
    public async Task Revocation_during_final_progression_scope_disposal_keeps_committed_scanned_state_without_handoff()
    {
        await using var s=await Setup.Create();s.Root.BeforeEffectDisposal=async()=>
        {var p=(await s.Read())!;if(p.Entries[0].State==ZipEntryState.Scanned)s.Root.Revoke();};
        await Denied(s.Scan());var after=(await s.Read())!;Assert.Equal(ZipEntryState.Scanned,after.Entries[0].State);NoDownstream(s.Fixture,after);
        s.Root.BeforeEffectDisposal=null;Assert.Equal(ZipScannerStop.AwaitingChildIngestion,(await s.Recover()).Stop);
    }
    [Fact]
    public async Task Raw_scanner_dependency_is_rejected_before_scanner_reservation()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();await using var root=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,f.Clock),root,await f.RequestAsync(root.Purpose),f.Clock);
        var materialized=await f.Runtime.Driver.RunAsync(attempt);await Assert.ThrowsAsync<NotSupportedException>(()=>f.Runtime.Driver.RunScannerAsync(attempt));
        Assert.Equal(materialized.Parent.Budget,(await f.Journal.ReadAsync(f.Admission.OperationId))!.Budget);
    }
    [Theory]
    [InlineData(ZipAdmissionEventKind.Revoked)][InlineData(ZipAdmissionEventKind.RequiresReview)]
    public async Task Durable_stop_between_reservation_and_child_read_denies_payload_access_with_live_root(ZipAdmissionEventKind kind)
    {
        await using var s=await Setup.Create();var f=s.Fixture;var reads=f.Runtime.Count("child-decrypt");
        f.Runtime.Scan.Checkpoint=async point=>{if(point=="Reserved")await f.Journal.AppendAdmissionEventAsync(f.Admission,new("before-read-stop",kind,"synthetic",f.Clock.Now,SafeReason:"Stopped"));};
        await Denied(s.Scan());var p=(await s.Read())!;Assert.Equal(1,p.Budget.ScannerAttempts);Assert.Equal(reads,f.Runtime.Count("child-decrypt"));
        Assert.Empty(s.Provider.Calls);Assert.Null(p.Entries[0].EvidenceJson);NoDownstream(f,p);
    }
    [Theory]
    [InlineData("revoke")][InlineData("deadline")]
    public async Task Invalidation_at_reservation_commit_rolls_back_rows_and_nonrefundable_charge_has_not_yet_occurred(string kind)
    {
        await using var s=await Setup.Create();var before=(await s.Read())!;var now=s.Fixture.Clock.Now;
        s.Fixture.Journal.ScanCheckpoint=point=>{if(point=="BeforeReservationCommit"){if(kind=="revoke")s.Root.Revoke();else s.Fixture.Clock.Now=now+ZipNumericLimits.AttemptTimeout;}return Task.CompletedTask;};
        await Denied(s.Scan());var after=(await s.Read())!;Assert.Equal(before.Fence,after.Fence);Assert.Equal(before.Budget,after.Budget);Assert.Empty(s.Provider.Calls);
        Assert.Null((await s.Fixture.Journal.ReadLatestScanAsync(after.Fence,0)).Attempt);
    }
    [Fact]
    public async Task Plaintext_read_cannot_start_at_original_scan_deadline_even_when_root_and_lease_are_valid()
    {
        await using var s=await Setup.Create();var start=s.Fixture.Clock.Now;var reads=s.Fixture.Runtime.Count("child-decrypt");
        s.Fixture.Runtime.Scan.Checkpoint=point=>{if(point=="Reserved")s.Fixture.Clock.Now=start+ZipNumericLimits.AttemptTimeout;return Task.CompletedTask;};
        await Denied(s.Scan());Assert.Equal(reads,s.Fixture.Runtime.Count("child-decrypt"));Assert.Empty(s.Provider.Calls);
        Assert.Equal(1,(await s.Read())!.Budget.ScannerAttempts);
    }
    [Fact]
    public async Task Two_interrupted_reserved_attempts_stop_without_third_submission_or_fabricated_result()
    {
        await using var s=await Setup.Create();var f=s.Fixture;var p=(await s.Read())!;
        var first=await f.Journal.BeginScanAsync(p.Fence,0,s.Provider.Policy,new ValidGuard());
        var second=await f.Journal.BeginScanAsync(first.Parent.Fence,0,s.Provider.Policy,new ValidGuard());
        var result=await s.Recover();Assert.Equal(ZipScannerStop.ScannerBudgetExhausted,result.Stop);
        Assert.Equal(second.Parent.Budget,result.Parent.Budget);Assert.Equal(2,result.Parent.Budget.ScannerAttempts);
        Assert.Null(result.Parent.Entries[0].EvidenceJson);Assert.Empty(s.Provider.Calls);NoDownstream(f,result.Parent);
    }
    [Theory]
    [InlineData("reservation")][InlineData("read")]
    public async Task Deadline_crossed_while_acquiring_live_ticket_denies_effect_after_acceptance_returns(string stage)
    {
        await using var s=await Setup.Create();var f=s.Fixture;var start=f.Clock.Now;var reads=f.Runtime.Count("child-decrypt");
        s.Root.AfterEffectAcceptance=request=>
        {if(stage=="reservation"&&request is EMF.Orchestration.Contracts.ZipScanReservationRequest||stage=="read"&&request is EMF.Orchestration.Contracts.ZipScanChildReadRequest)f.Clock.Now=start+ZipNumericLimits.AttemptTimeout;};
        await Denied(s.Scan());var p=(await s.Read())!;
        Assert.Equal(stage=="reservation"?0:1,p.Budget.ScannerAttempts);Assert.Equal(reads,f.Runtime.Count("child-decrypt"));Assert.Empty(s.Provider.Calls);Assert.Null(p.Entries[0].EvidenceJson);
    }
    [Fact]
    public async Task Durable_admission_stop_after_opened_plaintext_denies_held_lease_submission()
    {
        await using var s=await Setup.Create();var f=s.Fixture;
        f.Runtime.Scan.Checkpoint=async point=>{if(point=="Opened")await f.Journal.AppendAdmissionEventAsync(f.Admission,new("held-stop",ZipAdmissionEventKind.Revoked,"synthetic",f.Clock.Now,SafeReason:"Stop"));};
        await Denied(s.Scan());Assert.Empty(s.Provider.Calls);var p=(await s.Read())!;Assert.Null(p.Entries[0].EvidenceJson);
        Assert.All(f.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));NoDownstream(f,p);
    }
    [Fact]
    public async Task Guarded_scanner_with_opaque_transport_fails_preflight_before_reservation_or_protected_read()
    {
        var factory=ClamdMalwareScannerTests.Replies("stream: OK\0");var provider=new EMF.Security.Malware.ClamdMalwareScanner(factory,ClamdMalwareScannerTests.Profile());
        await using var f=await ZipOwnershipFixture.CreateAsync(scannerPolicy:provider.Policy);
        f.Runtime.Scan=new(f.Journal,f.Journal,f.Journal,f.Runtime.ChildRetention,provider);f.Runtime.ComposeDriver();
        await using var root=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,f.Clock),root,await f.RequestAsync(root.Purpose),f.Clock);
        var materialized=await f.Runtime.Driver.RunAsync(attempt);var reads=f.Runtime.Count("child-decrypt");
        await Assert.ThrowsAsync<NotSupportedException>(()=>f.Runtime.Driver.RunScannerAsync(attempt));
        Assert.Equal(materialized.Parent.Budget,(await f.Journal.ReadAsync(f.Admission.OperationId))!.Budget);Assert.Equal(reads,f.Runtime.Count("child-decrypt"));Assert.Equal(0,factory.Calls);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_guarded_clamd_text_or_nested_zip_OK_is_frozen_terminal_without_clean_progression(bool nested)
    {
        var version=new ClamdGuardedScannerTests.Connection("ClamAV 1.4.3/27897/date\0");var scan=new ClamdGuardedScannerTests.Connection("stream: OK\0");
        var factory=new ClamdGuardedScannerTests.Factory(version,scan);var provider=new EMF.Security.Malware.ClamdMalwareScanner(factory,ClamdMalwareScannerTests.Profile());
        var child=nested?ZipDurableRuntimeFixture.Zip():"child text payload"u8.ToArray();byte[] archive;
        using(var bytes=new MemoryStream())
        {using(var zip=new System.IO.Compression.ZipArchive(bytes,System.IO.Compression.ZipArchiveMode.Create,true)){using var entry=zip.CreateEntry(nested?"nested.zip":"text.txt").Open();entry.Write(child);}archive=bytes.ToArray();}
        await using var f=await ZipOwnershipFixture.CreateAsync(sourceBytes:archive,scannerPolicy:provider.Policy);
        f.Runtime.Scan=new(f.Journal,f.Journal,f.Journal,f.Runtime.ChildRetention,provider);f.Runtime.ComposeDriver();
        await using var root=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,f.Clock),root,await f.RequestAsync(root.Purpose),f.Clock);
        await f.Runtime.Driver.RunAsync(attempt);var result=await f.Runtime.Driver.RunScannerAsync(attempt);
        Assert.Equal(ZipScannerStop.AwaitingReviewAuthorization,result.Stop);Assert.Equal(ZipEntryState.Materialized,result.Parent.Entries[0].State);
        var exact=JsonSerializer.Deserialize<MalwareScanEvidence>(result.Parent.Entries[0].EvidenceJson!)!;
        Assert.Equal(MalwareCoverage.Unknown,exact.Coverage);Assert.Equal(MalwareScanFailure.CoverageUnproven,exact.Failure);Assert.False(exact.IsClean);
        Assert.True(scan.Aborted);Assert.True(scan.Disposed);NoDownstream(f,result.Parent);await AssertNoDurableDownstreamAsync(f,result.Parent);
        await using var recovery=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.Recovery,f.Clock);
        await using var recovered=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,f.Clock),recovery,await f.RequestAsync(recovery.Purpose),f.Clock);
        Assert.Equal(ZipScannerStop.AwaitingReviewAuthorization,(await f.Runtime.Driver.RunScannerAsync(recovered)).Stop);Assert.Equal(2,factory.Calls);
    }
    internal static async Task AssertNoDurableDownstreamAsync(ZipOwnershipFixture f,ZipParentSnapshot p)
    {
        var retainedParent=(await f.Journal.ReadParentRetentionAsync(p.Binding.OperationId))!;
        Assert.Equal(ZipRetentionState.Created,retainedParent.State);Assert.Null(retainedParent.ReleaseReceipt);Assert.Null(retainedParent.CandidateCleanup);
        foreach(var entry in p.Entries.Where(e=>e.Plan.FileOrdinal is not null))
        {
            var child=(await f.Journal.ReadRetentionAsync(p.Binding.OperationId,entry.Plan.FileOrdinal!.Value))!;
            Assert.Equal(ZipRetentionState.Created,child.State);Assert.Null(child.ReleaseReceipt);Assert.Null(child.CandidateCleanup);
        }
        await using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.Evidence.DatabasePath);await c.OpenAsync();
        foreach(var table in new[]{"ArtifactIngestionOperations","ArtifactIngestionIntents","ArtifactIngestionReviews","ZipExtractionAcknowledgements","Relationships","Artifacts"})
        {using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM "+table;Assert.Equal(0,Convert.ToInt64(await q.ExecuteScalarAsync()));}
    }
    internal sealed class ValidGuard:IZipEffectBoundaryGuard
    {
        public CancellationToken Invalidated=>CancellationToken.None;public ValueTask RevalidateAsync(CancellationToken ct=default){ct.ThrowIfCancellationRequested();return ValueTask.CompletedTask;}
        public IDisposable Accept(CancellationToken ct=default){ct.ThrowIfCancellationRequested();return new Accepted();}
        private sealed class Accepted:IDisposable{public void Dispose(){}}
    }
}
