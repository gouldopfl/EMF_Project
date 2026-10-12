using System.Security.Cryptography;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;

namespace EMF.Tests.TestInfrastructure;

// Trusted synthetic provider for local contract tests only. This is never stock
// routing, issuer provenance, analytic certification, or production scanner code.
internal sealed class SyntheticGuardedZipScanner(MalwareScannerPolicy policy):ZipGuardedMalwareScanner
{
    public override MalwareScannerPolicy Policy{get;}=policy;
    public override void ValidateGuardedDependencies() {}
    internal readonly Dictionary<string,int> Calls=[];
    internal Func<string,Task>? BeforeCall;
    internal Func<string,Task>? AfterCall {get;set;}
    internal Func<Task>? BeforeReply,BeforeDispose;
    internal Action? Submitted,Returned;
    internal byte[]? LastBuffer;
    internal bool ResponseClosed;
    internal Func<ZipScannerSubmission,Task<ZipScannerSubmission>>? TransformSubmission {get;set;}
    internal MalwareDetection Detection=MalwareDetection.NoThreatDetected;
    internal MalwareCoverage Coverage=MalwareCoverage.Complete;
    internal MalwareScanFailure Failure=MalwareScanFailure.None;
    public override Task<MalwareScanEvidence> ScanAsync(MalwareScanRequest request,Stream content,CancellationToken ct=default)
        =>throw new InvalidOperationException("Raw scanner must never be selected by authorized ZIP path.");
    public override async Task<ZipScannerSubmission> SubmitGuardedAsync(ZipScannerInvocation invocation,Stream content,IZipScannerCallAdmissionGuard guard,CancellationToken ct=default)
    {
        ValidateInvocation(invocation);var buffer=LastBuffer=new byte[ZipNumericLimits.ScannerChunk];long read=0;
        async Task Call(string kind,Func<ValueTask> actual,int length=0)
        {
            if(BeforeCall is not null)await BeforeCall(kind);
            var call=new ZipScannerCall(kind,read,length);await guard.RevalidateAsync(call,ct);ValueTask work;
            using(var admission=guard.AcceptCall(call,ct))
            {AdmitInvocation(invocation);Calls[kind]=Calls.GetValueOrDefault(kind)+1;work=actual();}
            await work;if(AfterCall is not null)await AfterCall(kind);
        }
        try
        {
            await Call("Connect",()=>ValueTask.CompletedTask);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while(read<invocation.Attempt.Request.Content.Length)
            {
                var count=(int)Math.Min(buffer.Length,invocation.Attempt.Request.Content.Length-read);var n=0;
                await Call("ChildRead",async()=>{n=await content.ReadAsync(buffer.AsMemory(0,count),ct);},count);
                if(n==0)throw new InvalidDataException("Short synthetic child.");
                hash.AppendData(buffer,0,n);await Call("Header",()=>ValueTask.CompletedTask,4);
                await Call("Payload",()=>ValueTask.CompletedTask,n);read+=n;
            }
            var extra=0;await Call("Probe",async()=>{extra=await content.ReadAsync(buffer.AsMemory(0,1),ct);},1);
            var observed=Convert.ToHexString(hash.GetHashAndReset());
            if(extra!=0||observed!=invocation.Attempt.Request.Content.Sha256)throw new InvalidDataException("Synthetic content changed.");
            await Call("Terminator",()=>ValueTask.CompletedTask,4);await Call("FinalFlush",()=>ValueTask.CompletedTask);
            Submitted?.Invoke();var result=new ZipScannerSubmission(null,new Response(this,invocation,read,observed));
            return TransformSubmission is null?result:await TransformSubmission(result);
        }
        catch(OperationCanceledException)
        {
            var e=new MalwareScanEvidence(invocation.Attempt.Request,MalwareDetection.Error,MalwareCoverage.Unknown,
                MalwareScanFailure.Cancelled,read,read,false,null,"Unknown","Unknown",invocation.Clock.GetUtcNow());
            return new(CompleteObservation(invocation,e,ZipScanObservationOrigin.AdmittedLocalFailure),null);
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }
    private sealed class Response(SyntheticGuardedZipScanner owner,ZipScannerInvocation invocation,long length,string hash):IZipScannerResponse
    {
        private bool disposed;private int used;public ZipScannerInvocation Invocation=>invocation;
        public async Task<ZipCompletedScanObservation> ReceiveObservationAsync(CancellationToken hardAbort=default)
        {
            if(disposed||Interlocked.Exchange(ref used,1)!=0)throw new UnauthorizedAccessException("Synthetic response already closed.");
            if(owner.BeforeReply is not null)await owner.BeforeReply();hardAbort.ThrowIfCancellationRequested();
            var e=new MalwareScanEvidence(invocation.Attempt.Request,owner.Detection,owner.Coverage,owner.Failure,
                length,length,true,hash,"synthetic-engine","synthetic-db",invocation.Clock.GetUtcNow(),
                owner.Coverage==MalwareCoverage.Complete?"synthetic-local-completeness-proof":null);
            var result=owner.CompleteObservation(invocation,e);owner.Returned?.Invoke();return result;
        }
        public async ValueTask DisposeAsync(){if(!disposed){disposed=true;owner.ResponseClosed=true;if(owner.BeforeDispose is not null)await owner.BeforeDispose();}}
    }
}
