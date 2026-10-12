using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Services;

public enum ZipScannerStop { AwaitingChildIngestion, ScannerRetryRequired, AwaitingReviewAuthorization, ScannerBudgetExhausted, OutsideScannerBoundary }
public sealed record ZipScannerResult(ZipScannerStop Stop,ZipParentSnapshot Parent,int? FileOrdinal=null);

public sealed partial class ZipChildScanService
{
    public async Task<ZipScannerResult> ScanNextAsync(ZipParentSnapshot parent,ZipExecutionAttempt attempt,
        CancellationToken ct=default,CancellationToken hardAbort=default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        // An unguarded scanner cannot consume a reservation or payload lease.
        var provider=scanner as IZipGuardedMalwareScanner??throw new NotSupportedException("Guarded ZIP scanner required.");
        provider.ValidateGuardedDependencies();retention.ValidateScanDependencies();
        using(var profile=System.Text.Json.JsonDocument.Parse(attempt.Admission.Binding.ProfileJson))
        {
            var p=profile.RootElement;
            if(provider.Policy!=new MalwareScannerPolicy(p.GetProperty("scannerProvider").GetString()!,p.GetProperty("scannerVersion").GetString()!,p.GetProperty("scannerPolicyHash").GetString()!))
                throw new InvalidDataException("Scanner policy differs from immutable admitted profile.");
        }
        attempt.EnterScannerInvocation();
        using var execution=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Invalidated);
        var token=execution.Token;await attempt.RevalidateAsync(token);
        await ZipChildWorkGate.Gate.WaitAsync(token);
        try
        {
            await attempt.RevalidateAsync(token);
            var current=await parents.ReadAsync(parent.Binding.OperationId,token)??throw new ZipFenceException();
            if(current.Fence!=parent.Fence)throw new ZipFenceException();parent=current;attempt.Validate(parent);
            if(parent.State is not (ZipParentState.Planned or ZipParentState.Processing))return await Stop(ZipScannerStop.OutsideScannerBoundary,null);
            var ordinal=checked(parent.Fence.ConfirmedOrdinal+1);
            var entry=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);
            if(entry is null||entry.State is not (ZipEntryState.Materialized or ZipEntryState.Scanned))
                return await Stop(ZipScannerStop.OutsideScannerBoundary,ordinal);
            var latest=await scans.ReadLatestScanAsync(parent.Fence,ordinal,token);
            if(latest.Evidence is { } frozen)
            {
                if(frozen.Request.Policy!=provider.Policy)throw new InvalidDataException("Frozen scanner policy differs from provider.");
                if(frozen.IsClean)return await Advance(latest.Attempt!,frozen,ordinal);
                if(ZipScanOutcome.IsTerminal(frozen,latest.ChargedAttempts))return await Stop(ZipScannerStop.AwaitingReviewAuthorization,ordinal);
                // A fresh bounded Recovery invocation may charge one retry.
                if(attempt.Admission.Approval is null)throw new UnauthorizedAccessException("Missing original approval.");
            }
            if(latest.ChargedAttempts>=ZipNumericLimits.Attempts)return await Stop(ZipScannerStop.ScannerBudgetExhausted,ordinal);
            if(entry.State!=ZipEntryState.Materialized)throw new InvalidDataException("Scanned child has no frozen Clean evidence.");
            var scanStart=attempt.Clock.GetUtcNow();
            using var scanDeadline=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout,attempt.Clock);
            using var submissionToken=CancellationTokenSource.CreateLinkedTokenSource(token,scanDeadline.Token);
            var submitCt=submissionToken.Token;
            ZipScanUpdate started;
            await using(var reservation=attempt.ScanReservationBoundary(()=>parent,ordinal,provider.Policy))
            {started=await scans.BeginScanAsync(parent.Fence,ordinal,provider.Policy,new ScanDeadlineGuard(reservation,scanStart+ZipNumericLimits.AttemptTimeout,attempt.Clock),submitCt);await reservation.FinishAsync(submitCt);}
            parent=started.Parent;await At("Reserved");
            var invocation=provider.CreateInvocation(started.Attempt,scanStart,attempt.Clock);
            await using var recorder=await attempt.IssueRecorderAsync(parent,invocation,submitCt);
            await using var calls=attempt.ScannerCalls(()=>parent,invocation,check=>scans.ValidateScanReadAsync(parent.Fence,started.Attempt,check));
            ZipScannerSubmission? submission=null;Exception? executionFailure=null;
            await using var responseOwner=new ResponseOwner(error=>executionFailure??=error);
            var read=attempt.ScanReadBoundary(()=>parent,started.Attempt,
                (await retention.ReadForScanAsync(parent,ordinal,submitCt))!);
            try
            {
                await using(var lease=await retention.OpenForScanAsync(parent,ordinal,new ScanReadEligibility(read,scans,parent.Fence,started.Attempt,attempt.Clock,invocation.ScanDeadlineUtc),submitCt))
                {
                    if(lease.ArtifactId.Value!=started.Attempt.Request.Content.ContentId||lease.Revision.Value!=started.Attempt.Request.Content.Revision||
                        lease.ReturnedLength!=started.Attempt.Request.Content.Length)throw new InvalidDataException("Scanner plaintext lease changed.");
                    using var source=lease.OpenReadStream();await At("Opened");
                    submission=await provider.SubmitGuardedAsync(invocation,source,calls,submitCt);
                    responseOwner.Response=submission.Response;await At("Submitted");
                }
            }
            catch(Exception error) when(submission is not null||invocation.SealedObservation is not null)
            {executionFailure??=error;submission??=new(invocation.SealedObservation,null);}
            finally
            {
                // Disposal always clears plaintext. Execution invalidation during
                // scope disposal must not erase an independently recordable result.
                try{await read.DisposeAsync();}catch(Exception error){executionFailure=error;}
            }
            try{await At("PlaintextCleared");}catch(Exception error){executionFailure??=error;}
            ZipCompletedScanObservation? observed=null;
            try
            {
                if(submission is null||(submission.Observation is null&&submission.Response is null))
                    throw new InvalidDataException("Scanner returned no submission outcome.");
                if(submission.Observation is not null&&submission.Response is not null)
                {
                    executionFailure??=new InvalidDataException("Scanner returned an ambiguous submission outcome.");
                    // An invalid envelope cannot erase an exact already sealed
                    // fact. Never receive twice; close the extraneous response.
                    observed=submission.Observation;await responseOwner.DisposeAsync();await At("ScannerReturned");
                }
                else if(submission.Response is { } response)
                {
                    try
                    {
                        if(!ReferenceEquals(response.Invocation,invocation))throw new UnauthorizedAccessException("Wrong response invocation.");
                        observed=await response.ReceiveObservationAsync(hardAbort);await At("ScannerReturned");
                    }
                    catch(Exception error) when(invocation.SealedObservation is { } sealedFact)
                    {observed=sealedFact;executionFailure??=error;}
                    finally{await responseOwner.DisposeAsync();}
                }
                else{observed=submission.Observation;await At("ScannerReturned");}
                if(observed is null)throw new InvalidDataException("No admitted scanner observation.");
                var recording=recorder.ForObservation(observed,parent.Fence);
                var remaining=recorder.ValidUntilUtc-attempt.Clock.GetUtcNow();
                if(remaining<=TimeSpan.Zero)throw new UnauthorizedAccessException("Recorder expired.");
                using var persistenceDeadline=new CancellationTokenSource(remaining<ZipNumericLimits.AttemptTimeout?remaining:ZipNumericLimits.AttemptTimeout,attempt.Clock);
                using var persistence=CancellationTokenSource.CreateLinkedTokenSource(hardAbort,recorder.Invalidated,persistenceDeadline.Token);
                parent=await scans.RecordScanObservationAsync(parent.Fence,recording,persistence.Token);await At("ObservationRecorded");
            }
            finally
            {
                try{await calls.FinishAsync(token);}catch(Exception error){executionFailure??=error;}
                try{await calls.DisposeAsync();}catch(Exception error){executionFailure??=error;}
            }
            // Recorder closure precedes final live validation/handoff; it is not
            // an execution scope and does not make root invalidation disappear.
            await recorder.DisposeAsync();
            if(executionFailure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(executionFailure).Throw();
            await attempt.RevalidateAsync(token);
            if(observed!.Evidence.IsClean)return await Advance(started.Attempt,observed.Evidence,ordinal);
            return await Stop(ZipScanOutcome.IsTerminal(observed.Evidence,latest.ChargedAttempts+1)
                ?ZipScannerStop.AwaitingReviewAuthorization:ZipScannerStop.ScannerRetryRequired,ordinal);

            async Task<ZipScannerResult> Advance(ZipScanAttempt scan,MalwareScanEvidence evidence,int file)
            {
                await using(var progression=attempt.ScanProgressionBoundary(()=>parent,scan,evidence))
                {parent=await scans.AdvanceRecordedCleanAsync(parent.Fence,scan,evidence,progression,token);await At("Progressed");await progression.FinishAsync(token);}
                return await Stop(ZipScannerStop.AwaitingChildIngestion,file);
            }
            async Task<ZipScannerResult> Stop(ZipScannerStop reason,int? file)
            {
                await attempt.RevalidateAsync(token);token.ThrowIfCancellationRequested();attempt.Validate(parent);
                return new(reason,parent,file);
            }
        }
        finally{ZipChildWorkGate.Gate.Release();}
    }
    // Refresh durable eligibility after lower-layer waits as well as live
    // authority. Final guarded progression still owns atomic admission vetoes;
    // this metadata read is not a distributed physical/journal transaction.
    private sealed class ScanReadEligibility(IZipEffectBoundaryGuard live,IZipScanJournal journal,
        ZipFence fence,ZipScanAttempt scan,TimeProvider clock,DateTimeOffset scanDeadline):IZipEffectBoundaryGuard
    {
        private DateTimeOffset evidenceUntil;
        public CancellationToken Invalidated=>live.Invalidated;
        public async ValueTask RevalidateAsync(CancellationToken ct=default)
        {if(clock.GetUtcNow()>=scanDeadline)throw new UnauthorizedAccessException("Scanner read deadline expired.");await live.RevalidateAsync(ct);evidenceUntil=await journal.ValidateScanReadAsync(fence,scan,ct);ct.ThrowIfCancellationRequested();}
        public IDisposable Accept(CancellationToken ct=default)
        {
            if(evidenceUntil<=clock.GetUtcNow()||scanDeadline<=clock.GetUtcNow())throw new UnauthorizedAccessException("Scanner read evidence expired.");
            var ticket=live.Accept(ct)??throw new UnauthorizedAccessException("Scanner read has no live acceptance.");
            try{ct.ThrowIfCancellationRequested();if(evidenceUntil<=clock.GetUtcNow()||scanDeadline<=clock.GetUtcNow())throw new UnauthorizedAccessException("Scanner read expired during acceptance.");return ticket;}
            catch{ticket.Dispose();throw;}
        }
    }
    private sealed class ScanDeadlineGuard(IZipEffectBoundaryGuard live,DateTimeOffset deadline,TimeProvider clock):IZipEffectBoundaryGuard
    {
        public CancellationToken Invalidated=>live.Invalidated;
        private void Check(){if(clock.GetUtcNow()>=deadline)throw new UnauthorizedAccessException("Scanner deadline expired.");}
        public async ValueTask RevalidateAsync(CancellationToken ct=default){Check();await live.RevalidateAsync(ct);Check();}
        public IDisposable Accept(CancellationToken ct=default)
        {Check();var ticket=live.Accept(ct)??throw new UnauthorizedAccessException("Scanner reservation has no acceptance.");try{ct.ThrowIfCancellationRequested();Check();return ticket;}catch{ticket.Dispose();throw;}}
    }
    private sealed class ResponseOwner(Action<Exception> failed):IAsyncDisposable
    {
        internal IZipScannerResponse? Response;
        private bool disposed;
        public async ValueTask DisposeAsync()
        {
            if(disposed)return;disposed=true;
            if(Response is not null)try{await Response.DisposeAsync();}catch(Exception error){failed(error);}
        }
    }

}
