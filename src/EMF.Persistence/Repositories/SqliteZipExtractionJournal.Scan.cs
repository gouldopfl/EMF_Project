using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;
namespace EMF.Persistence.Repositories;
public sealed partial class SqliteZipExtractionJournal
{
    private static MalwareScanContent ScanContent(ZipRetainedBinding r)=>new(r.ContentId,r.Revision,r.Sha256,r.Length);
    private static async Task ValidateScansAsync(SqliteConnection c,SqliteTransaction? tx,ZipParentSnapshot p,IReadOnlyList<ZipEntryProgress> entries,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT s.ReservationId,s.FileOrdinal,s.BindingJson,s.EvidenceJson,r.ReservationJson FROM ZipExtractionScans s LEFT JOIN ZipExtractionReservations r ON r.ParentOperationId=s.ParentOperationId AND r.ReservationId=s.ReservationId WHERE s.ParentOperationId=$op ORDER BY s.rowid";
        q.Parameters.AddWithValue("$op",p.Binding.OperationId);var clean=new HashSet<int>();var latest=new Dictionary<int,string>();var policies=new Dictionary<int,MalwareScannerPolicy>();
        using var rows=await q.ExecuteReaderAsync(ct);
        while(await rows.ReadAsync(ct))
        {
            var attempt=Decode<ZipScanAttempt>(rows.GetString(2));MalwareScanValidation.Request(attempt.Request);
            var entry=entries.SingleOrDefault(e=>e.Plan.FileOrdinal==attempt.FileOrdinal);
            if(rows.IsDBNull(4)||entry?.Retained is null||attempt.ReservationId!=rows.GetString(0)||attempt.FileOrdinal!=rows.GetInt32(1)||
                attempt.PlanHash!=p.Fence.PlanHash||attempt.Request.Content!=ScanContent(entry.Retained))throw new InvalidDataException("ZIP scan binding contradicts retained child.");
            var reservation=Decode<ZipWorkReservation>(rows.GetString(4));
            if(reservation.Kind!=ZipWorkKind.Scanner||reservation.Id!=attempt.ReservationId||reservation.FileOrdinal!=attempt.FileOrdinal||reservation.ExpandedBytes!=attempt.Request.Content.Length)
                throw new InvalidDataException("ZIP scan has no exact durable submission reservation.");
            if(policies.TryGetValue(attempt.FileOrdinal,out var policy)&&policy!=attempt.Request.Policy)throw new InvalidDataException("ZIP scanner policy changed across attempts.");
            policies[attempt.FileOrdinal]=attempt.Request.Policy;
            // A newer pending attempt also supersedes older Clean authority.
            clean.Remove(attempt.FileOrdinal);
            if(!rows.IsDBNull(3))
            {
                var evidence=Decode<MalwareScanEvidence>(rows.GetString(3));MalwareScanValidation.Evidence(evidence);
                if(evidence.Request!=attempt.Request)throw new InvalidDataException("ZIP scanner evidence contradicts attempt.");
                latest[attempt.FileOrdinal]=rows.GetString(3);
                if(evidence.IsClean)clean.Add(attempt.FileOrdinal);else clean.Remove(attempt.FileOrdinal);
            }
        }
        rows.Close();q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations r LEFT JOIN ZipExtractionScans s ON s.ParentOperationId=r.ParentOperationId AND s.ReservationId=r.ReservationId WHERE r.ParentOperationId=$op AND r.Kind=2 AND s.ReservationId IS NULL";
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0)throw new InvalidDataException("ZIP scanner reservation has no atomic policy binding.");
        if(entries.Any(e=>e.Plan.FileOrdinal is { } ordinal&&latest.TryGetValue(ordinal,out var evidence)&&e.EvidenceJson!=evidence))
            throw new InvalidDataException("ZIP progress contradicts immutable scanner evidence.");
        if(entries.Any(e=>e.State is ZipEntryState.Scanned or ZipEntryState.Ingested or ZipEntryState.Acknowledged or ZipEntryState.Released &&
            (e.Plan.FileOrdinal is not { } ordinal||!clean.Contains(ordinal))))throw new InvalidDataException("ZIP scanner state has no bound Clean evidence.");
    }
    public async Task<ZipScanUpdate> BeginScanAsync(ZipFence f,int ordinal,MalwareScannerPolicy policy,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(ordinal!=f.ConfirmedOrdinal+1||p.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new ZipFenceException();
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);
        if(entry.State!=ZipEntryState.Materialized||entry.Retained is null)throw new InvalidDataException("ZIP scan requires private materialization.");
        var request=new MalwareScanRequest(ScanContent(entry.Retained),policy);MalwareScanValidation.Request(request);
        var retention=await RetentionAsync(c,tx,f.OperationId,ordinal,ct);
        if(retention?.State!=ZipRetentionState.Created||retention.Identity.ObjectId!=entry.Retained.ContentId||retention.CreateReceipt!.CurrentRevision!.Value.Value!=entry.Retained.Revision||
            retention.Identity.PlaintextSha256!=entry.Retained.Sha256||retention.Identity.Length!=entry.Retained.Length)throw new InvalidDataException("ZIP scanner retention binding changed.");
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$file",ordinal);
        q.CommandText="SELECT BindingJson FROM ZipExtractionScans WHERE ParentOperationId=$op AND FileOrdinal=$file";
        using(var rows=await q.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))if(Decode<ZipScanAttempt>(rows.GetString(0)).Request.Policy!=policy)throw new InvalidDataException("ZIP scanner policy cannot change on replay.");
        q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=2";
        var attempts=Convert.ToInt32(await q.ExecuteScalarAsync(ct));if(attempts>=ZipNumericLimits.Attempts)throw new InvalidDataException("ZIP scanner attempts exhausted.");
        var budget=p.Budget with{ScannerAttempts=checked(p.Budget.ScannerAttempts+1),ScannerReserved=checked(p.Budget.ScannerReserved+entry.Plan.ExpandedLength),
            ReplayScanner=checked(p.Budget.ReplayScanner+(attempts>0?entry.Plan.ExpandedLength:0))};ValidateBudget(budget);
        var reservation=new ZipWorkReservation(Guid.NewGuid().ToString("N"),ZipWorkKind.Scanner,ordinal,entry.Plan.ExpandedLength);
        var attempt=new ZipScanAttempt(reservation.Id,ordinal,f.PlanHash!,request);q.Parameters.AddWithValue("$id",reservation.Id);q.Parameters.AddWithValue("$reservation",Json(reservation));q.Parameters.AddWithValue("$binding",Json(attempt));
        q.CommandText="INSERT INTO ZipExtractionReservations VALUES($op,$id,$file,2,$reservation); INSERT INTO ZipExtractionScans VALUES($op,$id,$file,$binding,NULL)";await q.ExecuteNonQueryAsync(ct);
        p=p with{Budget=budget,Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);tx.Commit();return new(p,attempt);
    }
    public async Task<ZipParentSnapshot> CompleteScanAsync(ZipFence f,ZipScanAttempt attempt,MalwareScanEvidence evidence,CancellationToken ct=default)
    {
        MalwareScanValidation.Evidence(evidence);if(evidence.Request!=attempt.Request)throw new InvalidDataException("ZIP scan result fingerprint/profile mismatch.");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$id",attempt.ReservationId);
        q.CommandText="SELECT BindingJson,EvidenceJson FROM ZipExtractionScans WHERE ParentOperationId=$op AND ReservationId=$id";
        using(var row=await q.ExecuteReaderAsync(ct))
        {
            if(!await row.ReadAsync(ct)||row.GetString(0)!=Json(attempt))throw new InvalidDataException("ZIP scan attempt binding changed.");
            if(!row.IsDBNull(1)){if(row.GetString(1)!=Json(evidence))throw new InvalidDataException("ZIP scan evidence is already frozen.");return p;}
        }
        if(attempt.FileOrdinal!=f.ConfirmedOrdinal+1||p.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new ZipFenceException();
        q.Parameters.AddWithValue("$file",attempt.FileOrdinal);
        q.CommandText="SELECT ReservationId FROM ZipExtractionScans WHERE ParentOperationId=$op AND FileOrdinal=$file ORDER BY rowid DESC LIMIT 1";
        if((string?)await q.ExecuteScalarAsync(ct)!=attempt.ReservationId)throw new ZipFenceException();
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==attempt.FileOrdinal);if(entry.State!=ZipEntryState.Materialized)throw new InvalidDataException("ZIP scan completion is not eligible.");
        q.Parameters.AddWithValue("$json",Json(evidence));q.CommandText="UPDATE ZipExtractionScans SET EvidenceJson=$json WHERE ParentOperationId=$op AND ReservationId=$id";await q.ExecuteNonQueryAsync(ct);
        q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=2";var count=Convert.ToInt32(await q.ExecuteScalarAsync(ct));
        var terminalReview=!evidence.IsClean&&(evidence.Detection!=MalwareDetection.Error||count>=2||evidence.Failure is not (MalwareScanFailure.TransportError or MalwareScanFailure.TimedOut or MalwareScanFailure.Cancelled));
        var next=entry with{State=evidence.IsClean?ZipEntryState.Scanned:terminalReview?ZipEntryState.RequiresReview:ZipEntryState.Materialized,
            EvidenceJson=Json(evidence),SafeReason=evidence.IsClean?null:"Scanner"+evidence.Failure};
        q.Parameters.AddWithValue("$progress",Json(next));q.CommandText="UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";await q.ExecuteNonQueryAsync(ct);
        p=p with{State=terminalReview?ZipParentState.RequiresReview:p.State,Budget=p.Budget with{RejectedEntries=checked(p.Budget.RejectedEntries+(terminalReview?1:0))},Fence=f with{Revision=checked(f.Revision+1)}};
        ValidateBudget(p.Budget);await SaveAsync(c,tx,p,ct);tx.Commit();return (await ReadAsync(f.OperationId,ct))!;
    }
}
