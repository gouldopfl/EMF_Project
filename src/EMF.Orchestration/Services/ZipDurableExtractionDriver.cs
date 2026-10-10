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
    internal Func<string,Task>? Checkpoint{get;set;}
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    public async Task<ZipParentSnapshot> ResumeAsync(string operation,string owner,TimeSpan lease,CancellationToken ct=default)
    {
        ZipParentSnapshot p;
        try{p=await parents.ClaimAsync(operation,owner,lease,ct);}
        catch(Exception error) when(error is InvalidDataException or System.Text.Json.JsonException or FormatException or ArgumentException)
        {return await parentJournal.ClaimParentEvidenceReviewAsync(operation,owner,lease,ct);}
        return await RunAsync(p,ct);
    }
    public async Task<ZipParentSnapshot> RunAsync(ZipParentSnapshot admitted,CancellationToken ct=default)
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
