using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Tests.TestInfrastructure;

internal sealed partial class SyntheticZipExecutionGrant
{
    internal bool DenyRecorder;
    internal bool DenyScanProgression;
    internal TimeSpan? RecorderLifetime;
    internal Action? RevokeRecorder;
    internal Action? BeforeRecordingAccept;
    internal Func<Task>? BeforeRecordingRevalidation {get;set;}
    internal Func<ZipScanCallRequest,Task>? BeforeScannerCall {get;set;}
    internal IZipScanObservationRecorder? LastRecorder;
    internal Func<IZipAuthorizedScannerCallScope,IZipAuthorizedScannerCallScope>? ChangeScannerCallScope {get;set;}
    public Task<IZipAuthorizedEffectScope<ZipScanReservationRequest>> AuthorizeScanReservationAsync(ZipScanReservationRequest request,CancellationToken ct=default)
        => IssueEffectAsync(request,request.Admission,ct);
    public Task<IZipAuthorizedEffectScope<ZipScanChildReadRequest>> AuthorizeScanChildReadAsync(ZipScanChildReadRequest request,CancellationToken ct=default)
        => IssueEffectAsync(request,request.Admission,ct);
    public Task<IZipAuthorizedEffectScope<ZipScanProgressionRequest>> AuthorizeScanProgressionAsync(ZipScanProgressionRequest request,CancellationToken ct=default)
        => DenyScanProgression?Task.FromException<IZipAuthorizedEffectScope<ZipScanProgressionRequest>>(new UnauthorizedAccessException("Synthetic progression denied.")):IssueEffectAsync(request,request.Admission,ct);
    public async Task<IZipAuthorizedScannerCallScope> AuthorizeScannerCallAsync(ZipScanCallRequest request,CancellationToken ct=default)
    {
        if(BeforeScannerCall is not null)await BeforeScannerCall(request);
        var inner=(EffectScope<ZipScanCallRequest>)await IssueEffectAsync(request,request.Admission,ct);
        IZipAuthorizedScannerCallScope scope=new CallScope(inner,request.Invocation.ScanDeadlineUtc,clock);return ChangeScannerCallScope?.Invoke(scope)??scope;
    }
    private sealed class CallScope(EffectScope<ZipScanCallRequest> inner,DateTimeOffset scanDeadline,TimeProvider clock):IZipAuthorizedScannerCallScope
    {
        public object Root=>inner.Root;public ZipScanCallRequest Request=>inner.Request;public string ExecutingActorId=>inner.ExecutingActorId;
        public DateTimeOffset ValidUntilUtc=>inner.ValidUntilUtc<scanDeadline?inner.ValidUntilUtc:scanDeadline;
        public CancellationToken Invalidated=>inner.Invalidated;
        private void Check(){if(clock.GetUtcNow()>=ValidUntilUtc)throw new UnauthorizedAccessException("Synthetic scanner call expired.");}
        public async ValueTask RevalidateAsync(CancellationToken ct=default){Check();await inner.RevalidateAsync(ct);Check();}
        public IDisposable AcceptCall(CancellationToken ct=default)
        {Check();var accepted=inner.Accept(ct);try{Check();return accepted;}catch{accepted.Dispose();throw;}}
        public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    public async Task<IZipScanObservationRecorder> AuthorizeScanObservationRecordingAsync(ZipScanRecordingRequest request,CancellationToken ct=default)
    {
        await RevalidateAsync(ct);
        if(DenyRecorder||request.Admission!=Admission)throw new UnauthorizedAccessException("Synthetic recorder denied.");
        var recorder=new Recorder(this,request,clock);
        LastRecorder=recorder;RevokeRecorder=recorder.Revoke;return recorder;
    }
    private sealed class Recorder:IZipScanObservationRecorder
    {
        private readonly SyntheticZipExecutionGrant owner;private readonly TimeProvider clock;
        private readonly CancellationTokenSource invalidation=new();private readonly object gate=new();private bool disposed;
        public object Root{get;}public ZipScanRecordingRequest Request{get;}public string ExecutingActorId{get;}
        public DateTimeOffset ValidUntilUtc{get;}public CancellationToken Invalidated=>invalidation.Token;
        internal Recorder(SyntheticZipExecutionGrant owner,ZipScanRecordingRequest request,TimeProvider clock)
        {
            this.owner=owner;this.clock=clock;Root=owner;Request=request;ExecutingActorId=owner.ExecutingActorId;
            var ceiling=request.Invocation.ScanDeadlineUtc+ZipNumericLimits.AttemptTimeout;
            var desired=owner.RecorderLifetime is {} d?clock.GetUtcNow()+d:ceiling;
            ValidUntilUtc=desired<ceiling?desired:ceiling;
        }
        public void Revoke(){lock(gate)invalidation.Cancel();}
        private void Check(CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Invalidated.ThrowIfCancellationRequested();if(disposed||clock.GetUtcNow()>=ValidUntilUtc)throw new UnauthorizedAccessException("Recorder expired/disposed.");}
        public async ValueTask RevalidateAsync(CancellationToken ct=default)
        {Check(ct);if(owner.BeforeRecordingRevalidation is not null)await owner.BeforeRecordingRevalidation();Check(ct);}
        public IDisposable AcceptRecording(ZipCompletedScanObservation observation,ZipFence fence,CancellationToken ct=default)
        {
            owner.BeforeRecordingAccept?.Invoke();Monitor.Enter(gate);
            try
            {
                Check(ct);
                if(!ReferenceEquals(observation.Invocation,Request.Invocation)||!observation.Invocation.Owns(observation)||
                    fence.OperationId!=Request.Parent.Fence.OperationId||fence.Owner!=Request.Parent.Fence.Owner||fence.Epoch!=Request.Parent.Fence.Epoch)
                    throw new UnauthorizedAccessException("Recorder provenance changed.");
                return new Accepted(gate);
            }
            catch{Monitor.Exit(gate);throw;}
        }
        private sealed class Accepted(object gate):IDisposable{public void Dispose()=>Monitor.Exit(gate);}
        public ValueTask DisposeAsync(){if(!disposed){disposed=true;invalidation.Cancel();}return ValueTask.CompletedTask;}
    }
}
