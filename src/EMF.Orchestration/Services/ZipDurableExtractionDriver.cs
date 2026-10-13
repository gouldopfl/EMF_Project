using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
namespace EMF.Orchestration.Services;
// Explicit host composition supplies governed scanner/ingestion capabilities.
// Legacy workflow entry points are not silently redirected into this driver.
public sealed class ZipDurableExtractionDriver(IZipExtractionJournal parents,IZipParentRetentionJournal parentJournal,
    IZipRetentionJournal childJournal,IZipExtractionExecutionJournal work,IZipAcknowledgementJournal acknowledgements,
    ZipParentRetentionService parentRetention,ZipProtectedRetentionService childRetention,ZipEntryPlanner planner,
    ZipSequentialExtractor extractor,ZipChildScanService scanner,ZipChildIngestionService ingestion)
{
    // Snapshots and concurrency claims are not executable authority.
    public Task<ZipParentSnapshot> RunAsync(ZipParentSnapshot snapshot,CancellationToken ct=default)
        => Task.FromException<ZipParentSnapshot>(new UnauthorizedAccessException("A live authorized ZIP attempt is required."));
    public Task<ZipParentSnapshot> ResumeAsync(string operation,string owner,TimeSpan lease,CancellationToken ct=default)
        => Task.FromException<ZipParentSnapshot>(new UnauthorizedAccessException("Recovery requires a fresh authorized ZIP attempt."));
    public async Task<ZipPhaseIIResult> RunAsync(ZipExecutionAttempt attempt,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Invalidated);ct=linked.Token;
        await attempt.RevalidateAsync(ct);
        var p=await parents.ReadAsync(attempt.Claimed.Binding.OperationId,ct)??throw new InvalidDataException("Missing ZIP parent.");
        attempt.Validate(p);
        if(p.Fence!=attempt.Claimed.Fence)throw new ZipFenceException();
        IArtifactContentReadLease? input=null;
        IDisposable? permit=null;
        ZipPhaseIIResult result;
        try
        {
            if(p.State is ZipParentState.RequiresReview or ZipParentState.Completed or ZipParentState.Released)
                result=new(ZipPhaseIIStop.OutsidePhaseIIBoundary,p);
            else
            {
                permit=await ZipParentReadAdmission.ProcessWide.AcquireAsync(ct);
                await attempt.RevalidateAsync(ct);
                var fresh=await parents.ReadAsync(p.Binding.OperationId,ct)??throw new ZipFenceException();
                if(fresh.Fence!=p.Fence)throw new ZipFenceException();attempt.Validate(fresh);
                if(p.State==ZipParentState.AdmissionPending)
                {
                    (p,input)=await parentRetention.OpenAsync(p,attempt,ct);
                    await At("ParentOpened");
                    p=await planner.AdmitAsync(parents,p,input,attempt,ct);await At("A");
                }
                await attempt.RevalidateAsync(ct);
                var ordinal=checked(p.Fence.ConfirmedOrdinal+1);
                var entry=p.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);
                if(entry is null)result=new(ZipPhaseIIStop.AwaitingLaterCompletion,p);
                else if(entry.State==ZipEntryState.Materialized)result=new(ZipPhaseIIStop.AwaitingScanner,p,ordinal);
                else if(entry.State!=ZipEntryState.Planned)result=new(ZipPhaseIIStop.OutsidePhaseIIBoundary,p,ordinal);
                else
                {
                    var existing=await childJournal.ReadRetentionAsync(p.Binding.OperationId,ordinal,ct);
                    if(existing is not null&&existing.State!=ZipRetentionState.Reserved)
                    {
                        var recovered=await childRetention.SealAsync(p,ordinal,null,attempt,ct);p=recovered.Parent;
                        await using var guard=attempt.ExtractionBoundary(()=>p,ordinal);
                        p=await work.MaterializedAsync(p.Fence,ordinal,recovered.Retention,guard,ct);
                        await guard.FinishAsync(ct);
                    }
                    else
                    {
                        if(input is null)(p,input)=await parentRetention.OpenAsync(p,attempt,ct);
                        await At("BeforeExtraction");
                        p=await extractor.MaterializeNextAsync(p,input,attempt,ct);
                    }
                    await At("B");
                    if(p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal).State!=ZipEntryState.Materialized)
                        throw new InvalidDataException("ZIP materialization did not reach its covered stop.");
                    result=new(ZipPhaseIIStop.AwaitingScanner,p,ordinal);
                }
            }
        }
        catch(Exception error) when(error is InvalidDataException or IOException or System.Security.Cryptography.CryptographicException)
        {result=new(ZipPhaseIIStop.EvidenceFailure,p,SafeReason:"PhaseIIEvidenceFailure");}
        finally{try{if(input is not null)await input.DisposeAsync();}finally{permit?.Dispose();}}
        // No scan reservation/read/submission, ingestion, ACK, review, completion,
        // release or cleanup exists on this path. Materialized implies no permission.
        await attempt.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();attempt.Validate(p);
        return result;
    }
    public async Task<ZipScannerResult> RunScannerAsync(ZipExecutionAttempt attempt,CancellationToken ct=default,CancellationToken hardAbort=default)
    {
        ArgumentNullException.ThrowIfNull(attempt);await attempt.RevalidateAsync(ct);
        var p=await parents.ReadAsync(attempt.Claimed.Binding.OperationId,ct)??throw new InvalidDataException("Missing ZIP parent.");
        attempt.Validate(p);
        // Legitimate Phase II progress may have advanced revision under this
        // same owner/epoch. The scanner service/journal use the exact reread.
        return await scanner.ScanNextAsync(p,attempt,ct,hardAbort);
    }
    public async Task<ZipChildIngestionResult> RunChildIngestionAsync(ZipExecutionAttempt attempt,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        await attempt.RevalidateAsync(ct);
        var p=await parents.ReadAsync(attempt.Claimed.Binding.OperationId,ct)
            ??throw new InvalidDataException("Missing ZIP parent.");
        attempt.Validate(p);
        // The child service re-reads and owns every effect. This entry cannot
        // acknowledge, release, classify or advance the ZIP frontier.
        return await ingestion.IngestNextAsync(p,attempt,ct);
    }
    public async Task<ZipAcknowledgementResult> RunAcknowledgementAsync(ZipExecutionAttempt attempt,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        attempt.EnterAcknowledgementInvocation();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Invalidated);ct=linked.Token;
        await attempt.RevalidateAsync(ct);
        var parent=await parents.ReadAsync(attempt.Claimed.Binding.OperationId,ct)??throw new ZipFenceException();
        attempt.Validate(parent);
        if(parent.State is not (ZipParentState.Planned or ZipParentState.Processing))
            return await Stop(ZipAcknowledgementStop.OutsideAcknowledgementBoundary,null,null);
        var next=checked(parent.Fence.ConfirmedOrdinal+1);
        var entry=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==next);
        var already=false;
        int ordinal;
        if(entry?.State==ZipEntryState.Ingested&&entry.Ingestion is not null)
            ordinal=next;
        else if(attempt.Purpose==ZipOwnershipPurpose.Recovery&&parent.Fence.ConfirmedOrdinal>=0&&
            parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==parent.Fence.ConfirmedOrdinal) is {State:ZipEntryState.Acknowledged,Ingestion:not null})
        {
            ordinal=parent.Fence.ConfirmedOrdinal;already=true;
            entry=parent.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);
        }
        else return await Stop(ZipAcknowledgementStop.OutsideAcknowledgementBoundary,next,null);
        // Keep this precommit projection stable until the scope is finished.
        // The guarded transaction itself rereads all durable eligibility.
        await using var guard=attempt.AcknowledgementBoundary(()=>parent,ordinal);
        await guard.RevalidateAsync(ct);
        var request=new ZipAcknowledgementAuthorityRequest(attempt.Admission,parent.Binding,parent.Fence,
            entry!.Plan,entry.Retained!,entry.Ingestion!,attempt.ExecutingActorId,attempt.Purpose,attempt.ValidUntilUtc);
        var result=await acknowledgements.AcknowledgeEntryAsync(request,guard,ct);
        await guard.FinishAsync(ct);
        parent=result.Parent;
        if(result.AlreadyAcknowledged!=already||parent.Entries.Single(e=>e.Plan.FileOrdinal==ordinal).State!=ZipEntryState.Acknowledged)
            throw new InvalidDataException("ZIP acknowledgement result differs from the admitted occurrence.");
        return await Stop(ZipAcknowledgementStop.Acknowledged,ordinal,result);

        async Task<ZipAcknowledgementResult> Stop(ZipAcknowledgementStop reason,int? file,ZipAcknowledgementUpdate? update)
        {
            await attempt.RevalidateAsync(ct);ct.ThrowIfCancellationRequested();attempt.Validate(parent);
            return new(reason,parent,file,update?.AlreadyAcknowledged??false,update?.ContainsId,update?.DerivedFromId);
        }
    }
    public async Task<ZipProtectedReleaseResult> RunProtectedReleaseAsync(ZipExecutionAttempt attempt,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        attempt.EnterProtectedReleaseInvocation();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Invalidated);ct=linked.Token;
        await attempt.RevalidateAsync(ct);
        var parent=await parents.ReadAsync(attempt.Claimed.Binding.OperationId,ct)??throw new ZipFenceException();
        attempt.Validate(parent);
        if(parent.State is not (ZipParentState.Planned or ZipParentState.Processing)||
            parent.Fence.ConfirmedOrdinal<0)
            return new(ZipProtectedReleaseStop.OutsideProtectedReleaseBoundary,parent,null);
        var ordinal=parent.Fence.ConfirmedOrdinal;
        var entry=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);
        if(entry?.State!=ZipEntryState.Acknowledged)
            return new(ZipProtectedReleaseStop.OutsideProtectedReleaseBoundary,parent,ordinal);
        var released=await childRetention.ReleaseProtectedThroughReceiptAsync(parent,ordinal,attempt,ct);
        if(released.Retention.State!=ZipRetentionState.ReleasePending||released.Retention.ReleaseReceipt is null||
            released.Retention.CandidateCleanup is not null||
            released.Parent.Entries.Single(e=>e.Plan.FileOrdinal==ordinal).State!=ZipEntryState.Acknowledged)
            throw new InvalidDataException("ZIP protected release crossed its receipt stop.");
        await attempt.RevalidateAsync(ct);attempt.Validate(released.Parent);
        return new(ZipProtectedReleaseStop.ReleaseReceiptRecorded,released.Parent,ordinal);
    }
    internal Func<string,Task>? Checkpoint{get;set;}
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    internal async Task<ZipParentSnapshot> ResumeLegacyTestHarnessAsync(string operation,string owner,TimeSpan lease,CancellationToken ct=default)
    {
        ZipParentSnapshot p;
        try{p=await parents.ClaimAsync(operation,owner,lease,ct);}
        catch(Exception error) when(error is InvalidDataException or System.Text.Json.JsonException or FormatException or ArgumentException)
        {return await parentJournal.ClaimParentEvidenceReviewAsync(operation,owner,lease,ct);}
        return await RunLegacyTestHarnessAsync(p,ct);
    }
    internal async Task<ZipParentSnapshot> RunLegacyTestHarnessAsync(ZipParentSnapshot admitted,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();ZipParentSnapshot p;
        try{p=await parents.ReadAsync(admitted.Binding.OperationId,ct)??throw new InvalidDataException("Missing ZIP operation.");}
        catch(Exception error) when(error is InvalidDataException or System.Text.Json.JsonException or FormatException or ArgumentException)
        {using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);await parentJournal.ReviewParentEvidenceAsync(admitted.Fence,"ParentRecoveryEvidenceFailure",review.Token);throw;}
        if(p.Fence!=admitted.Fence)throw new ZipFenceException();
        // No parent plaintext lifetime can overlap another driver allocation.
        using var permit=await ZipParentReadAdmission.ProcessWide.AcquireAsync(ct);IArtifactContentReadLease? input=null;
        try
        {
            if(p.State==ZipParentState.RequiresReview)return p;
            var owned=await parentJournal.ReadParentRetentionAsync(p.Binding.OperationId,ct)??throw new InvalidDataException("Missing parent retention evidence.");
            if(owned.State==ZipRetentionState.RequiresReview||p.State==ZipParentState.RequiresReview)throw new InvalidDataException("ZIP operation requires review.");
            if(p.State==ZipParentState.Released)return (await parentRetention.ReleaseAsync(p,ct)).Parent;
            if(p.State==ZipParentState.AdmissionPending){(p,input)=await parentRetention.OpenAsync(p,ct);p=await planner.AdmitAsync(parents,p,input,ct);await At("A");}
            // Confirmed children may still need receipt reconciliation/release after
            // process death. This does not reopen the parent or resubmit child work.
            foreach(var e in p.Entries.Where(e=>e.Plan.FileOrdinal is { } ordinal&&ordinal<=p.Fence.ConfirmedOrdinal))
            {var r=await childJournal.ReadRetentionAsync(p.Binding.OperationId,e.Plan.FileOrdinal!.Value,ct)??throw new InvalidDataException("Missing confirmed child retention.");if(r.State!=ZipRetentionState.Released)p=(await childRetention.ReleaseAsync(p,e.Plan.FileOrdinal.Value,ct)).Parent;}
            while(p.State is ZipParentState.Planned or ZipParentState.Processing)
            {
                ct.ThrowIfCancellationRequested();var ordinal=checked(p.Fence.ConfirmedOrdinal+1);var e=p.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);if(e is null)break;
                if(e.State==ZipEntryState.Planned)
                {
                    var retained=await childJournal.ReadRetentionAsync(p.Binding.OperationId,ordinal,ct);
                    if(retained is not null&&retained.State!=ZipRetentionState.Reserved)
                    {var recovered=await childRetention.SealAsync(p,ordinal,null,ct);p=await work.MaterializedAsync(recovered.Parent.Fence,ordinal,recovered.Retention,ct);}
                    else{if(input is null)(p,input)=await parentRetention.OpenAsync(p,ct);p=await extractor.MaterializeNextAsync(p,input,ct);}
                    await At("B");e=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);
                }
                // Reuse one admitted parent lease across sequential children. A
                // fresh runtime opens it only when unfinished extraction needs it;
                // every open is durably charged before protected allocation.
                if(e.State==ZipEntryState.Materialized){p=await scanner.ScanNextAsync(p,ct);if(p.State==ZipParentState.RequiresReview)return p;await At("C");e=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);if(e.State!=ZipEntryState.Scanned)return p;}
                if(e.State is ZipEntryState.Scanned or ZipEntryState.Ingested){p=await ingestion.IngestNextAsync(p,ct);await At("D");e=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);}
                if(e.State!=ZipEntryState.Ingested||e.Ingestion is null)throw new InvalidDataException("Missing authoritative child ingestion proof.");
                p=(await acknowledgements.AcknowledgeEntryAsync(p.Fence,ordinal,e.Ingestion,ct)).Parent;await At("F");
                p=(await childRetention.ReleaseAsync(p,ordinal,ct)).Parent;
            }
            if(input is not null){await input.DisposeAsync();input=null;}
            await At("G");p=await parentJournal.CompleteParentAsync(p.Fence,ct);await At("Completed");return (await parentRetention.ReleaseAsync(p,ct)).Parent;
        }
        catch(Exception error) when(error is InvalidDataException or System.Security.Cryptography.CryptographicException)
        {using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);try{await parentJournal.ReviewParentEvidenceAsync(p.Fence,"ZipRecoveryEvidenceFailure",review.Token);}catch(ZipFenceException){/* A newer state/owner cannot be reviewed using this stale fence. */}throw;}
        finally{if(input is not null)await input.DisposeAsync();}
    }
}

// Transient stop results, never new durable states or grants.
public enum ZipPhaseIIStop { AwaitingScanner, AwaitingLaterCompletion, OutsidePhaseIIBoundary, EvidenceFailure }
public sealed record ZipPhaseIIResult(ZipPhaseIIStop Stop,ZipParentSnapshot Parent,int? FileOrdinal=null,string? SafeReason=null);
public enum ZipAcknowledgementStop { Acknowledged, OutsideAcknowledgementBoundary }
public sealed record ZipAcknowledgementResult(ZipAcknowledgementStop Stop,ZipParentSnapshot Parent,
    int? FileOrdinal=null,bool AlreadyAcknowledged=false,long? ContainsId=null,long? DerivedFromId=null);
public enum ZipProtectedReleaseStop { ReleaseReceiptRecorded, OutsideProtectedReleaseBoundary }
public sealed record ZipProtectedReleaseResult(ZipProtectedReleaseStop Stop,ZipParentSnapshot Parent,int? FileOrdinal=null);
