using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipScanObservationAuthorityTests
{
    private sealed class Calls:IZipScannerCallAdmissionGuard
    {
        public CancellationToken Invalidated=>CancellationToken.None;
        public ValueTask RevalidateAsync(ZipScannerCall call,CancellationToken ct=default){ct.ThrowIfCancellationRequested();return ValueTask.CompletedTask;}
        public IDisposable AcceptCall(ZipScannerCall call,CancellationToken ct=default){ct.ThrowIfCancellationRequested();return new Acceptance();}
        private sealed class Acceptance:IDisposable{public void Dispose(){}}
    }
    private sealed record Prepared(ZipParentSnapshot Parent,ZipScanAttempt Scan,ZipScannerInvocation Invocation,
        ZipCompletedScanObservation Observation,ZipScanRecorderLease Recorder);
    private static async Task<Prepared> Prepare(ZipScannerAuthorityTests.Setup s)
    {
        var f=s.Fixture;var parent=(await s.Read())!;
        var begin=await f.Journal.BeginScanAsync(parent.Fence,0,s.Provider.Policy,new ZipScannerAuthorityTests.ValidGuard());
        var invocation=s.Provider.CreateInvocation(begin.Attempt,f.Clock.Now,f.Clock);
        var recorder=await s.Attempt.IssueRecorderAsync(begin.Parent,invocation,CancellationToken.None);
        using var source=new MemoryStream("synthetic durable child payload"u8.ToArray());
        var submission=await s.Provider.SubmitGuardedAsync(invocation,source,new Calls());await using var response=submission.Response!;
        var observed=await response.ReceiveObservationAsync();return new(begin.Parent,begin.Attempt,invocation,observed,recorder);
    }
    [Fact]
    public async Task Exact_observation_replay_requires_refreshed_fence_and_still_valid_recorder()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var p=await Prepare(s);await using var recorder=p.Recorder;
        var before=p.Parent;var first=await s.Fixture.Journal.RecordScanObservationAsync(before.Fence,recorder.ForObservation(p.Observation,before.Fence));
        Assert.Equal(before.Budget,first.Budget);Assert.Equal(before.State,first.State);Assert.Equal(ZipEntryState.Materialized,first.Entries[0].State);
        await Assert.ThrowsAsync<ZipFenceException>(()=>s.Fixture.Journal.RecordScanObservationAsync(before.Fence,recorder.ForObservation(p.Observation,before.Fence)));
        var again=await s.Fixture.Journal.RecordScanObservationAsync(first.Fence,recorder.ForObservation(p.Observation,first.Fence));
        Assert.Equal(first.Fence,again.Fence);Assert.Equal(first.Budget,again.Budget);
        var replayGuard=recorder.ForObservation(p.Observation,first.Fence);s.Root.RevokeRecorder!();
        await ZipScannerAuthorityTests.Denied(s.Fixture.Journal.RecordScanObservationAsync(first.Fence,replayGuard));
    }
    [Fact]
    public async Task Another_invocation_cannot_supply_evidence_even_when_request_and_payload_match()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var p=await Prepare(s);await using var recorder=p.Recorder;
        var other=s.Provider.CreateInvocation(p.Scan,s.Fixture.Clock.Now,s.Fixture.Clock);
        using var source=new MemoryStream("synthetic durable child payload"u8.ToArray());var submitted=await s.Provider.SubmitGuardedAsync(other,source,new Calls());
        await using var response=submitted.Response!;var substituted=await response.ReceiveObservationAsync();
        Assert.Equal(p.Observation.Evidence.Request,substituted.Evidence.Request);
        Assert.Throws<UnauthorizedAccessException>(()=>recorder.ForObservation(substituted,p.Parent.Fence));
        Assert.Null((await s.Read())!.Entries[0].EvidenceJson);Assert.Empty(typeof(ZipCompletedScanObservation).GetConstructors());
    }
    [Fact]
    public async Task Stale_worker_after_real_ownership_takeover_cannot_record_or_merge_observation()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var p=await Prepare(s);await using var recorder=p.Recorder;var f=s.Fixture;
        var beforeRecord=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Journal.ScanCheckpoint=point=>{if(point=="BeforeRecordTransaction"){beforeRecord.TrySetResult();return release.Task;}return Task.CompletedTask;};
        var recording=f.Journal.RecordScanObservationAsync(p.Parent.Fence,recorder.ForObservation(p.Observation,p.Parent.Fence));
        await beforeRecord.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var other=new EMF.Persistence.Repositories.SqliteZipExtractionJournal(f.Evidence.DatabasePath,f.Clock);await other.InitializeAsync();
        var takeover=await other.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.Recovery));
        release.SetResult();await Assert.ThrowsAsync<ZipFenceException>(()=>recording);
        var after=(await s.Read())!;Assert.Equal(takeover.Fence,after.Fence);Assert.Equal(takeover.Budget,after.Budget);Assert.Null(after.Entries[0].EvidenceJson);
    }
    [Theory]
    [InlineData("revision")][InlineData("latest")][InlineData("lease")]
    public async Task Factual_recorder_cannot_override_current_durable_recording_eligibility(string veto)
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var p=await Prepare(s);await using var recorder=p.Recorder;var f=s.Fixture;
        if(veto=="lease")
        {
            await using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.Evidence.DatabasePath);await c.OpenAsync();
            using var q=c.CreateCommand();q.CommandText="UPDATE ZipExtractionParents SET OwnerUntil=$until WHERE OperationId=$op";
            q.Parameters.AddWithValue("$until",f.Clock.Now.ToString("O"));q.Parameters.AddWithValue("$op",f.Admission.OperationId);await q.ExecuteNonQueryAsync();
        }
        else if(veto=="latest")await f.Journal.BeginScanAsync(p.Parent.Fence,0,s.Provider.Policy,new ZipScannerAuthorityTests.ValidGuard());
        else await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.Recovery));
        var before=(await s.Read())!;
        var recordFence=veto=="latest"?before.Fence:p.Parent.Fence;
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.RecordScanObservationAsync(recordFence,recorder.ForObservation(p.Observation,recordFence)));
        var after=(await s.Read())!;Assert.Equal(before.Fence,after.Fence);Assert.Equal(before.Budget,after.Budget);Assert.Null(after.Entries[0].EvidenceJson);
    }
    [Fact]
    public async Task Independent_expiry_while_record_transaction_waits_rolls_back_all_observation_fields()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();s.Root.RecorderLifetime=TimeSpan.FromSeconds(30);
        var p=await Prepare(s);await using var recorder=p.Recorder;var f=s.Fixture;
        f.Journal.ScanCheckpoint=point=>{if(point=="BeforeRecordCommit")f.Clock.Now=recorder.ValidUntilUtc;return Task.CompletedTask;};
        await ZipScannerAuthorityTests.Denied(f.Journal.RecordScanObservationAsync(p.Parent.Fence,recorder.ForObservation(p.Observation,p.Parent.Fence)));
        var after=(await s.Read())!;Assert.Equal(p.Parent.Fence,after.Fence);Assert.Equal(p.Parent.Budget,after.Budget);Assert.Null(after.Entries[0].EvidenceJson);
    }
    [Fact]
    public async Task Recorder_deadline_is_absolute_from_scan_start_and_never_slides_with_arrival_or_replay()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var p=await Prepare(s);await using var recorder=p.Recorder;
        Assert.Equal(p.Invocation.ScanStartUtc+TimeSpan.FromSeconds(240),recorder.ValidUntilUtc);
        s.Fixture.Clock.Now+=TimeSpan.FromSeconds(30);var first=await s.Fixture.Journal.RecordScanObservationAsync(p.Parent.Fence,recorder.ForObservation(p.Observation,p.Parent.Fence));
        Assert.Equal(p.Invocation.ScanStartUtc+TimeSpan.FromSeconds(240),recorder.ValidUntilUtc);
        s.Fixture.Clock.Now=recorder.ValidUntilUtc;
        Assert.ThrowsAny<Exception>(()=>recorder.ForObservation(p.Observation,first.Fence));Assert.Equal(first.Fence,(await s.Read())!.Fence);
    }
    [Theory]
    [InlineData("revoke")][InlineData("evidenceExpiry")]
    public async Task Final_progression_acceptance_checks_live_authority_and_durable_evidence_independently(string invalidation)
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var f=s.Fixture;
        if(invalidation=="evidenceExpiry")
        {
            f.Admission=await f.Journal.AppendAdmissionEventAsync(f.Admission,new("short-current-evidence",ZipAdmissionEventKind.AuthorityValidated,"synthetic",f.Clock.Now,
                f.Admission.Approval! with{DecisionId="short",IssuedUtc=f.Clock.Now,ExpiresUtc=f.Clock.Now.AddMinutes(1)}));
        }
        f.Journal.ScanCheckpoint=point=>
        {if(point=="BeforeProgressionCommit"){if(invalidation=="revoke")s.Root.Revoke();else f.Clock.Now=f.Admission.LatestAuthorityEvidence!.ExpiresUtc;}return Task.CompletedTask;};
        await ZipScannerAuthorityTests.Denied(s.Scan());var after=(await s.Read())!;
        Assert.NotNull(after.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,after.Entries[0].State);
        Assert.Equal(1,after.Budget.ScannerAttempts);ZipScannerAuthorityTests.NoDownstream(f,after);
    }
    [Fact]
    public async Task Invalid_recording_admission_at_exact_commit_keeps_charged_reservation_without_fact()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();s.Root.BeforeRecordingAccept=()=>s.Root.RevokeRecorder!();
        await ZipScannerAuthorityTests.Denied(s.Scan());var after=(await s.Read())!;
        Assert.Equal(1,after.Budget.ScannerAttempts);Assert.Null(after.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,after.Entries[0].State);
    }
    [Theory]
    [InlineData(MalwareDetection.MalwareDetected,MalwareCoverage.Unknown,MalwareScanFailure.Threat,false)]
    [InlineData(MalwareDetection.NoThreatDetected,MalwareCoverage.Unknown,MalwareScanFailure.CoverageUnproven,false)]
    [InlineData(MalwareDetection.Error,MalwareCoverage.Complete,MalwareScanFailure.TimedOut,true)]
    public async Task Local_origin_cannot_manufacture_provider_verdict_or_analytic_completeness(MalwareDetection detection,MalwareCoverage coverage,MalwareScanFailure failure,bool proof)
    {
        var provider=new ImproperLocalProvider(detection,coverage,failure,proof);
        var request=new MalwareScanRequest(new("synthetic","revision",new string('A',64),1),provider.Policy);
        var invocation=provider.CreateInvocation(new("reservation",0,new string('B',64),request),DateTimeOffset.UtcNow,TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(()=>provider.SubmitGuardedAsync(invocation,Stream.Null,new Calls()));Assert.Null(invocation.SealedObservation);
    }
    private sealed class ImproperLocalProvider(MalwareDetection detection,MalwareCoverage coverage,MalwareScanFailure failure,bool proof):ZipGuardedMalwareScanner
    {
        public override MalwareScannerPolicy Policy=>new("synthetic","1",new string('C',64));
        public override Task<MalwareScanEvidence> ScanAsync(MalwareScanRequest request,Stream content,CancellationToken ct=default)=>throw new NotSupportedException();
        public override async Task<ZipScannerSubmission> SubmitGuardedAsync(ZipScannerInvocation invocation,Stream content,IZipScannerCallAdmissionGuard admission,CancellationToken ct=default)
        {
            var call=new ZipScannerCall("SyntheticAdmittedOperation");await admission.RevalidateAsync(call,ct);
            using(var ticket=ZipScannerAdmission.Accept(admission,call,ct))AdmitInvocation(invocation);
            var e=new MalwareScanEvidence(invocation.Attempt.Request,detection,coverage,failure,1,1,true,invocation.Attempt.Request.Content.Sha256,
                "synthetic","synthetic",DateTimeOffset.UtcNow,proof?"unearned-completeness-proof":null);
            return new(CompleteObservation(invocation,e,ZipScanObservationOrigin.AdmittedLocalFailure),null);
        }
    }
    [Fact]
    public async Task Failed_recorder_issuance_disposes_independent_timer_and_cancellation_resources()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();var clock=new ClamdGuardedScannerTests.ManualClock(f.Clock.Now);
        await using var root=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,clock),root,await f.RequestAsync(root.Purpose),clock);
        await f.Runtime.Driver.RunAsync(attempt);var parent=(await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var provider=new SyntheticGuardedZipScanner(f.Profile.ScannerPolicy);var begin=await f.Journal.BeginScanAsync(parent.Fence,0,provider.Policy,new ZipScannerAuthorityTests.ValidGuard());
        var invocation=provider.CreateInvocation(begin.Attempt,clock.Now,clock);var activeBefore=clock.ActiveTimers;
        root.BeforeRecordingRevalidation=()=>{root.Revoke();return Task.CompletedTask;};
        await ZipScannerAuthorityTests.Denied(attempt.IssueRecorderAsync(begin.Parent,invocation,CancellationToken.None));
        Assert.Equal(activeBefore,clock.ActiveTimers);Assert.True(root.LastRecorder!.Invalidated.IsCancellationRequested);
        Assert.Null((await f.Journal.ReadAsync(f.Admission.OperationId))!.Entries[0].EvidenceJson);
    }
    [Fact]
    public async Task Attempt_enforces_scanner_scope_deadline_even_when_issuer_expiry_notification_is_delayed()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();var clock=new ClamdGuardedScannerTests.ManualClock(f.Clock.Now);
        await using var root=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(new(f.Journal,f.Journal,f.Profile,clock),root,await f.RequestAsync(root.Purpose),clock);
        await f.Runtime.Driver.RunAsync(attempt);var parent=(await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var provider=new SyntheticGuardedZipScanner(f.Profile.ScannerPolicy);var begin=await f.Journal.BeginScanAsync(parent.Fence,0,provider.Policy,new ZipScannerAuthorityTests.ValidGuard());
        var invocation=provider.CreateInvocation(begin.Attempt,clock.Now,clock);root.EffectLifetime=TimeSpan.FromSeconds(5);
        root.ChangeScannerCallScope=scope=>new DelayedExpirySignal(scope);
        var calls=attempt.ScannerCalls(()=>begin.Parent,invocation,ct=>f.Journal.ValidateScanReadAsync(begin.Parent.Fence,begin.Attempt,ct));
        try
        {
            var call=new ZipScannerCall("Payload",0,1);await calls.RevalidateAsync(call);using(calls.AcceptCall(call)){}
            Assert.False(calls.Invalidated.IsCancellationRequested);clock.Advance(TimeSpan.FromSeconds(5));
            Assert.True(calls.Invalidated.IsCancellationRequested);Assert.True(attempt.Invalidated.IsCancellationRequested);
            Assert.False(root.Revoked.IsCancellationRequested);Assert.ThrowsAny<Exception>(()=>calls.AcceptCall(call));
        }
        finally{await Assert.ThrowsAnyAsync<Exception>(()=>calls.DisposeAsync().AsTask());}
    }
    private sealed class DelayedExpirySignal(EMF.Orchestration.Contracts.IZipAuthorizedScannerCallScope inner):EMF.Orchestration.Contracts.IZipAuthorizedScannerCallScope
    {
        public object Root=>inner.Root;public EMF.Orchestration.Contracts.ZipScanCallRequest Request=>inner.Request;public string ExecutingActorId=>inner.ExecutingActorId;
        public DateTimeOffset ValidUntilUtc=>inner.ValidUntilUtc;public CancellationToken Invalidated=>CancellationToken.None;
        public ValueTask RevalidateAsync(CancellationToken ct=default)=>inner.RevalidateAsync(ct);public IDisposable AcceptCall(CancellationToken ct=default)=>inner.AcceptCall(ct);
        public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    private sealed class ForwardGuard(IZipScanObservationGuard inner):IZipScanObservationGuard
    {
        public ZipParentAdmission Admission=>inner.Admission;public ZipParentBinding Parent=>inner.Parent;public ZipFence ExpectedFence=>inner.ExpectedFence;
        public ZipCompletedScanObservation Observation=>inner.Observation;public CancellationToken Invalidated=>inner.Invalidated;
        public ValueTask RevalidateAsync(CancellationToken ct=default)=>inner.RevalidateAsync(ct);public IDisposable Accept(CancellationToken ct=default)=>inner.Accept(ct);
    }
}
