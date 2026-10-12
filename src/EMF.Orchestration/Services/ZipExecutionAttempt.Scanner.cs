using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed partial class ZipExecutionAttempt
{
    private int scannerInvocationUsed;
    internal void EnterScannerInvocation()
    {if(Interlocked.Exchange(ref scannerInvocationUsed,1)!=0)throw new UnauthorizedAccessException("A new scanner invocation requires a fresh Recovery attempt.");}
    internal ZipPhaseIIBoundary<ZipScanReservationRequest> ScanReservationBoundary(Func<ZipParentSnapshot> current,int ordinal,MalwareScannerPolicy policy)
        => Boundary(()=>{var p=current();Validate(p);return new ZipScanReservationRequest(admission,p,p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal).Plan,policy);},
            (g,r,ct)=>g.AuthorizeScanReservationAsync(r,ct));
    internal ZipPhaseIIBoundary<ZipScanChildReadRequest> ScanReadBoundary(Func<ZipParentSnapshot> current,ZipScanAttempt scan,ZipRetentionRecord retention)
        => Boundary(()=>{var p=current();Validate(p);return new ZipScanChildReadRequest(admission,p,scan,retention);},
            (g,r,ct)=>g.AuthorizeScanChildReadAsync(r,ct));
    internal ZipPhaseIIBoundary<ZipScanProgressionRequest> ScanProgressionBoundary(Func<ZipParentSnapshot> current,ZipScanAttempt scan,MalwareScanEvidence evidence)
        => Boundary(()=>{var p=current();Validate(p);return new ZipScanProgressionRequest(admission,p,scan,evidence);},
            (g,r,ct)=>g.AuthorizeScanProgressionAsync(r,ct));
    internal ZipScannerCallBoundary ScannerCalls(Func<ZipParentSnapshot> current,ZipScannerInvocation invocation,Func<CancellationToken,Task<DateTimeOffset>> eligible)
        => new(this,root,actor,current,invocation,eligible);
    internal async Task<ZipScanRecorderLease> IssueRecorderAsync(ZipParentSnapshot parent,ZipScannerInvocation invocation,CancellationToken ct)
    {
        Validate(parent);await RevalidateAsync(ct);
        var request=new ZipScanRecordingRequest(admission,parent,invocation);
        var recorder=await root.AuthorizeScanObservationRecordingAsync(request,ct);
        ZipScanRecorderLease? lease=null;
        try
        {
            lease=new ZipScanRecorderLease(recorder,root,actor,request,Clock);
            await lease.ValidateAsync(ct);await RevalidateAsync(ct);ct.ThrowIfCancellationRequested();return lease;
        }
        catch { if(lease is not null)await lease.DisposeAsync();else await recorder.DisposeAsync();throw; }
    }
}

internal sealed class ZipScannerCallBoundary(ZipExecutionAttempt attempt,IZipAuthorizedExecution root,string actor,
    Func<ZipParentSnapshot> current,ZipScannerInvocation invocation,Func<CancellationToken,Task<DateTimeOffset>> eligible) : IZipScannerCallAdmissionGuard,IAsyncDisposable
{
    private IZipAuthorizedScannerCallScope? scope;
    private ZipScanCallRequest? issued;
    private DateTimeOffset deadline,evidenceUntil;
    private bool disposed;
    private readonly CancellationTokenSource lifetime=CancellationTokenSource.CreateLinkedTokenSource(attempt.Invalidated);
    private CancellationTokenRegistration registration,deadlineRegistration;
    private CancellationTokenSource? scopeDeadline;
    public CancellationToken Invalidated=>lifetime.Token;
    public async ValueTask RevalidateAsync(ZipScannerCall call,CancellationToken ct=default)
    {
        if(disposed)throw new UnauthorizedAccessException("Scanner call boundary disposed.");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,Invalidated);ct=linked.Token;
        try
        {
            await ReleaseAsync();await attempt.RevalidateAsync(ct);var p=current();attempt.Validate(p);
            issued=new(attempt.Admission,p,invocation,call);
            scope=await root.AuthorizeScannerCallAsync(issued,ct);deadline=scope.ValidUntilUtc;
            if(deadline>attempt.ValidUntilUtc||deadline>invocation.ScanDeadlineUtc)
                throw new UnauthorizedAccessException("Scanner call deadline exceeds execution/scan limit.");
            var remaining=deadline-attempt.Clock.GetUtcNow();
            if(remaining<=TimeSpan.Zero)throw new UnauthorizedAccessException("Scanner call expired.");
            scopeDeadline=new(remaining,attempt.Clock);
            registration=scope.Invalidated.Register(()=>{lifetime.Cancel();attempt.Invalidate();});
            deadlineRegistration=scopeDeadline.Token.Register(()=>{lifetime.Cancel();attempt.Invalidate();});
            await scope.RevalidateAsync(ct);await attempt.RevalidateAsync(ct);evidenceUntil=await eligible(ct);Check(call,ct);
        }
        catch {attempt.Invalidate();throw;}
    }
    private void Check(ZipScannerCall call,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();attempt.Check();Invalidated.ThrowIfCancellationRequested();
        if(disposed||scope is null||issued is null||scope.Request!=issued||!ReferenceEquals(scope.Root,root)||
            scope.ExecutingActorId!=actor||scope.ValidUntilUtc!=deadline||attempt.Clock.GetUtcNow()>=deadline||
            issued.Call!=call||issued.Parent!=current()||attempt.Clock.GetUtcNow()>=evidenceUntil)throw new UnauthorizedAccessException("Scanner call anchors changed.");
        scope.Invalidated.ThrowIfCancellationRequested();scopeDeadline!.Token.ThrowIfCancellationRequested();
    }
    public IDisposable AcceptCall(ZipScannerCall call,CancellationToken ct=default)
    {
        try
        {
            Check(call,ct);var accepted=scope!.AcceptCall(ct)??throw new UnauthorizedAccessException("Scanner call has no acceptance.");
            try{Check(call,ct);return accepted;}catch{accepted.Dispose();throw;}
        }
        catch{attempt.Invalidate();throw;}
    }
    private async ValueTask ReleaseAsync()
    {
        var old=scope;if(old is null)return;
        var token=old.Invalidated;var bound=issued;var until=deadline;
        try
        {
            await old.DisposeAsync();token.ThrowIfCancellationRequested();
            if(old.Request!=bound||!ReferenceEquals(old.Root,root)||old.ExecutingActorId!=actor||old.ValidUntilUtc!=until||attempt.Clock.GetUtcNow()>=until)
                throw new UnauthorizedAccessException("Scanner call changed/expired during disposal.");
            attempt.Check();
        }
        finally{registration.Dispose();deadlineRegistration.Dispose();scopeDeadline?.Dispose();scopeDeadline=null;scope=null;issued=null;}
    }
    internal async Task FinishAsync(CancellationToken ct)
    {
        try{await ReleaseAsync();await attempt.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();}
        catch{attempt.Invalidate();throw;}
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;
        try{await ReleaseAsync();}
        catch{attempt.Invalidate();throw;}
        finally{disposed=true;lifetime.Dispose();}
    }
}

// Independently bounded: recording never calls attempt.Check/Revalidate and
// never links root/scan/caller cancellation after issuance.
internal sealed class ZipScanRecorderLease : IAsyncDisposable
{
    private readonly IZipScanObservationRecorder recorder;
    private readonly object root;
    private readonly string actor;
    private readonly ZipScanRecordingRequest request;
    private readonly TimeProvider clock;
    private readonly DateTimeOffset until;
    private readonly CancellationTokenSource deadline,lifetime;
    private ZipCompletedScanObservation? sealedObservation;
    private bool disposed;
    internal CancellationToken Invalidated=>lifetime.Token;
    internal DateTimeOffset ValidUntilUtc=>until;
    internal ZipScanRecorderLease(IZipScanObservationRecorder recorder,object root,string actor,ZipScanRecordingRequest request,TimeProvider clock)
    {
        this.recorder=recorder;this.root=root;this.actor=actor;this.request=request;this.clock=clock;until=recorder.ValidUntilUtc;
        var remaining=until-clock.GetUtcNow();
        if(until>request.Invocation.ScanDeadlineUtc+ZipNumericLimits.AttemptTimeout||remaining<=TimeSpan.Zero)
            throw new UnauthorizedAccessException("Factual recorder deadline is invalid.");
        deadline=new(remaining,clock);lifetime=CancellationTokenSource.CreateLinkedTokenSource(recorder.Invalidated,deadline.Token);
        try{Check();}catch{lifetime.Dispose();deadline.Dispose();throw;}
    }
    private void Check()
    {
        Invalidated.ThrowIfCancellationRequested();
        if(disposed||!ReferenceEquals(recorder.Root,root)||recorder.Request!=request||recorder.ExecutingActorId!=actor||
            recorder.ValidUntilUtc!=until||clock.GetUtcNow()>=until)
            throw new UnauthorizedAccessException("Factual recorder anchor changed or expired.");
    }
    internal async ValueTask ValidateAsync(CancellationToken ct)
    {Check();await recorder.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();Check();}
    internal IZipScanObservationGuard ForObservation(ZipCompletedScanObservation observation,ZipFence fence)
    {
        Check();
        if(!ReferenceEquals(observation.Invocation,request.Invocation)||!observation.Invocation.Owns(observation)||
            observation.Evidence.Request!=request.Invocation.Attempt.Request||
            fence.OperationId!=request.Parent.Fence.OperationId||fence.Owner!=request.Parent.Fence.Owner||
            fence.Epoch!=request.Parent.Fence.Epoch||fence.PlanHash!=request.Parent.Fence.PlanHash||
            fence.ConfirmedOrdinal!=request.Parent.Fence.ConfirmedOrdinal)
            throw new UnauthorizedAccessException("Observation provenance or ownership differs.");
        if(sealedObservation is not null&&!ReferenceEquals(sealedObservation,observation))throw new UnauthorizedAccessException("Recorder already sealed.");
        sealedObservation=observation;return new RecordingGuard(this,observation,fence);
    }
    private sealed class RecordingGuard(ZipScanRecorderLease owner,ZipCompletedScanObservation observation,ZipFence fence):IZipScanObservationGuard
    {
        public ZipParentAdmission Admission=>owner.request.Admission;
        public ZipParentBinding Parent=>owner.request.Parent.Binding;
        public ZipFence ExpectedFence=>fence;
        public ZipCompletedScanObservation Observation=>observation;
        public CancellationToken Invalidated=>owner.Invalidated;
        public ValueTask RevalidateAsync(CancellationToken ct=default)=>owner.ValidateAsync(ct);
        public IDisposable Accept(CancellationToken ct=default)
        {
            ct.ThrowIfCancellationRequested();owner.Check();
            var accepted=owner.recorder.AcceptRecording(observation,fence,ct)??throw new UnauthorizedAccessException("Recorder returned no acceptance.");
            try{owner.Check();ct.ThrowIfCancellationRequested();return accepted;}catch{accepted.Dispose();throw;}
        }
    }
    public async ValueTask DisposeAsync()
    {try{if(!disposed)await recorder.DisposeAsync();}finally{disposed=true;lifetime.Dispose();deadline.Dispose();}}
}
