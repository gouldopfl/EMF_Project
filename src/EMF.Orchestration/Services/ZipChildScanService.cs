using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
namespace EMF.Orchestration.Services;
public sealed class ZipChildScanService(IZipExtractionJournal parents,IZipScanJournal scans,
    IZipExtractionExecutionJournal failures,ZipProtectedRetentionService retention,IMalwareScanner scanner)
{
    internal Func<string,Task>? Checkpoint { get; set; }
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    public async Task<ZipParentSnapshot> ScanNextAsync(ZipParentSnapshot parent,CancellationToken ct=default)
    {
        await ZipChildWorkGate.Gate.WaitAsync(ct);
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(ZipNumericLimits.AttemptTimeout);var token=deadline.Token;
            var current=await parents.ReadAsync(parent.Binding.OperationId,token);if(current?.Fence!=parent.Fence)throw new ZipFenceException();parent=current;
            if(parent.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new InvalidOperationException("ZIP scanning is not eligible.");
            var ordinal=checked(parent.Fence.ConfirmedOrdinal+1);var entry=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);
            if(entry is null||entry.State is ZipEntryState.Scanned or ZipEntryState.Ingested)return parent;
            if(entry.State!=ZipEntryState.Materialized)throw new InvalidOperationException("ZIP scanning requires materialized child.");
            try
            {
                var started=await scans.BeginScanAsync(parent.Fence,ordinal,scanner.Policy,token);parent=started.Parent;await At("Reserved");
                MalwareScanEvidence evidence;
                // Close the stream and clear the owned plaintext before recording any scanner success.
                await using(var lease=await retention.OpenAsync(parent,ordinal,token))
                {
                    if(lease.ArtifactId.Value!=started.Attempt.Request.Content.ContentId||lease.Revision.Value!=started.Attempt.Request.Content.Revision||
                        lease.ReturnedLength!=started.Attempt.Request.Content.Length)throw new InvalidDataException("ZIP scanner plaintext lease changed.");
                    using var stream=lease.OpenReadStream();await At("Opened");
                    try{evidence=await scanner.ScanAsync(started.Attempt.Request,stream,token);}
                    catch(OperationCanceledException)
                    {
                        evidence=new(started.Attempt.Request,MalwareDetection.Error,MalwareCoverage.Unknown,
                            ct.IsCancellationRequested?MalwareScanFailure.Cancelled:MalwareScanFailure.TimedOut,0,0,false,null,"Unknown","Unknown",DateTimeOffset.UtcNow);
                    }
                    catch(IOException)
                    {evidence=new(started.Attempt.Request,MalwareDetection.Error,MalwareCoverage.Unknown,MalwareScanFailure.TransportError,0,0,false,null,"Unknown","Unknown",DateTimeOffset.UtcNow);}
                    await At("ScannerReturned");
                }
                await At("PlaintextCleared");
                // Persistence and cleanup must still run after caller/attempt cancellation.
                using var persistence=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);
                parent=await scans.CompleteScanAsync(parent.Fence,started.Attempt,evidence,persistence.Token);await At("Completed");
                ct.ThrowIfCancellationRequested();return parent;
            }
            catch(InvalidDataException)
            {
                using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);var latest=await parents.ReadAsync(parent.Binding.OperationId,review.Token);
                if(latest?.Fence==parent.Fence&&latest.State is (ZipParentState.Planned or ZipParentState.Processing))
                    await failures.RejectExtractionAsync(parent.Fence,ordinal,"ScannerEvidenceRejected",review.Token);
                throw;
            }
        }
        finally{ZipChildWorkGate.Gate.Release();}
    }
}
