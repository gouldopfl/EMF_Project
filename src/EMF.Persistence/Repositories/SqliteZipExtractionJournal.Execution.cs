using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;
namespace EMF.Persistence.Repositories;
public sealed partial class SqliteZipExtractionJournal
{
    private static ZipBudget AddCharge(ZipBudget b,ZipExtractionCharge r)=>b with
    {ExpandedProduced=checked(b.ExpandedProduced+r.ExpandedBytes),ParentReadBytes=checked(b.ParentReadBytes+r.ParentReadBytes),CrcBytes=checked(b.CrcBytes+r.CrcBytes)};
    private static async Task<ZipBudget> ReconstructChargesAsync(SqliteConnection c,SqliteTransaction? tx,string op,ZipBudget b,ZipPlan? plan,IReadOnlyList<ZipEntryProgress> entries,ZipRetainedBinding parentInput,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT c.ChargeJson,c.ChargeId,c.ReservationId,c.FileOrdinal,r.ReservationJson FROM ZipExtractionCharges c LEFT JOIN ZipExtractionReservations r ON r.ParentOperationId=c.ParentOperationId AND r.ReservationId=c.ReservationId WHERE c.ParentOperationId=$op";
        q.Parameters.AddWithValue("$op",op);using var rows=await q.ExecuteReaderAsync(ct);
        var totals=new Dictionary<string,(long Expanded,long Crc)>();
        while(await rows.ReadAsync(ct))
        {
            var charge=Decode<ZipExtractionCharge>(rows.GetString(0));
            // The reserved -1 occurrence charges a complete retained-parent read
            // before protected payload/decryption allocation. No child reservation.
            if(charge.FileOrdinal==-1)
            {
                if(!rows.IsDBNull(4)||charge.Id!=rows.GetString(1)||charge.ReservationId!=rows.GetString(2)||rows.GetInt32(3)!=-1||
                    string.IsNullOrWhiteSpace(charge.Id)||charge.ReservationId!=parentInput.Revision||charge.ExpandedBytes!=0||charge.CrcBytes!=0||charge.ParentReadBytes!=parentInput.Length)
                    throw new InvalidDataException("Retained parent read charge changed.");
                b=AddCharge(b,charge);continue;
            }
            if(rows.IsDBNull(4)||charge.Id!=rows.GetString(1)||charge.ReservationId!=rows.GetString(2)||charge.FileOrdinal!=rows.GetInt32(3))throw new InvalidDataException("ZIP charge identity/binding changed.");
            var reservation=Decode<ZipWorkReservation>(rows.GetString(4));ValidateCharge(charge,reservation);
            totals.TryGetValue(charge.ReservationId,out var sum);sum=(checked(sum.Expanded+charge.ExpandedBytes),checked(sum.Crc+charge.CrcBytes));totals[charge.ReservationId]=sum;
            if(sum.Expanded>reservation.ExpandedBytes||sum.Crc>reservation.ExpandedBytes)throw new InvalidDataException("ZIP attempt work exceeds its reservation.");
            b=AddCharge(b,charge);
        }
        rows.Close();
        q.CommandText="SELECT v.FileOrdinal,v.VerificationJson,r.ReservationJson FROM ZipExtractionVerifications v LEFT JOIN ZipExtractionReservations r ON r.ParentOperationId=v.ParentOperationId AND r.ReservationId=json_extract(v.VerificationJson,'$.ReservationId') WHERE v.ParentOperationId=$op";
        var verified=new HashSet<int>();
        using(var proofs=await q.ExecuteReaderAsync(ct))while(await proofs.ReadAsync(ct))
        {
            var proof=Decode<ZipVerifiedChild>(proofs.GetString(1));var entry=entries.SingleOrDefault(e=>e.Plan.FileOrdinal==proof.FileOrdinal);
            if(proofs.IsDBNull(2)||proof.FileOrdinal!=proofs.GetInt32(0)||entry is null||plan is null||proof.PlanHash!=plan.Hash||
                proof.Length!=entry.Plan.ExpandedLength||proof.Crc32!=entry.Plan.Crc32||entry.Plan.IsEncrypted||proof.Sha256 is null||proof.Sha256.Length!=64||
                proof.Sha256.Any(ch=>!char.IsAsciiHexDigit(ch)||char.IsAsciiLetterLower(ch)))throw new InvalidDataException("ZIP persisted integrity proof contradicts plan.");
            var reservation=Decode<ZipWorkReservation>(proofs.GetString(2));totals.TryGetValue(proof.ReservationId,out var sum);
            if(reservation.Id!=proof.ReservationId||reservation.Kind!=ZipWorkKind.Extraction||reservation.FileOrdinal!=proof.FileOrdinal||sum.Expanded!=proof.Length||sum.Crc!=proof.Length)
                throw new InvalidDataException("ZIP persisted integrity proof has no fully charged extraction.");
            if(entry.State is ZipEntryState.Materialized or ZipEntryState.Scanned or ZipEntryState.Ingested or ZipEntryState.Acknowledged or ZipEntryState.Released &&
                (entry.Retained is null||entry.Retained.Length!=proof.Length||entry.Retained.Sha256!=proof.Sha256))throw new InvalidDataException("ZIP retained progress contradicts integrity proof.");
            verified.Add(proof.FileOrdinal);
        }
        if(entries.Any(e=>e.State is ZipEntryState.Materialized or ZipEntryState.Scanned or ZipEntryState.Ingested or ZipEntryState.Acknowledged or ZipEntryState.Released &&
            (e.Plan.FileOrdinal is not { } ordinal||!verified.Contains(ordinal))))throw new InvalidDataException("ZIP materialized progress has no durable integrity proof.");
        ValidateBudget(b);return b;
    }
    private static void ValidateCharge(ZipExtractionCharge charge,ZipWorkReservation reservation)
    {
        if(string.IsNullOrWhiteSpace(charge.Id)||reservation.Kind!=ZipWorkKind.Extraction||charge.FileOrdinal!=reservation.FileOrdinal||
            charge.ExpandedBytes<0||charge.ParentReadBytes<0||charge.CrcBytes<0||charge.ExpandedBytes!=charge.CrcBytes||
            charge.ExpandedBytes>reservation.ExpandedBytes||charge.ParentReadBytes>ZipNumericLimits.ParentReads)
            throw new InvalidDataException("ZIP work charge exceeds admitted attempt.");
    }
    public async Task<ZipParentSnapshot> ChargeAsync(ZipFence f,ZipExtractionCharge charge,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(p.State is not (ZipParentState.Planned or ZipParentState.Processing)||charge.FileOrdinal!=f.ConfirmedOrdinal+1)throw new ZipFenceException();
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$id",charge.Id);
        q.CommandText="SELECT ChargeJson FROM ZipExtractionCharges WHERE ParentOperationId=$op AND ChargeId=$id";
        if(await q.ExecuteScalarAsync(ct) is string old){if(old!=Json(charge))throw new InvalidDataException("ZIP charge identity reused.");return p;}
        q.Parameters.AddWithValue("$reservation",charge.ReservationId);
        q.CommandText="SELECT ReservationJson FROM ZipExtractionReservations WHERE ParentOperationId=$op AND ReservationId=$reservation";
        var reservation=Decode<ZipWorkReservation>((string?)await q.ExecuteScalarAsync(ct)??throw new InvalidDataException("ZIP work has no durable reservation."));ValidateCharge(charge,reservation);
        q.CommandText="SELECT ChargeJson FROM ZipExtractionCharges WHERE ParentOperationId=$op AND ReservationId=$reservation";
        long expanded=charge.ExpandedBytes,crc=charge.CrcBytes;
        using(var rows=await q.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct)){var r=Decode<ZipExtractionCharge>(rows.GetString(0));expanded=checked(expanded+r.ExpandedBytes);crc=checked(crc+r.CrcBytes);}
        if(expanded>reservation.ExpandedBytes||crc>reservation.ExpandedBytes)throw new InvalidDataException("ZIP per-attempt charged work exhausted.");
        var budget=AddCharge(p.Budget,charge);ValidateBudget(budget);
        q.Parameters.AddWithValue("$file",charge.FileOrdinal);q.Parameters.AddWithValue("$json",Json(charge));
        q.CommandText="INSERT INTO ZipExtractionCharges VALUES($op,$id,$reservation,$file,$json)";await q.ExecuteNonQueryAsync(ct);
        p=p with{Budget=budget,State=ZipParentState.Processing,Fence=p.Fence with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);tx.Commit();return p;
    }
    public async Task<ZipParentSnapshot> VerifyChildAsync(ZipFence f,ZipVerifiedChild child,CancellationToken ct=default)
    {
        if(child.Sha256 is null||child.Sha256.Length!=64||child.Sha256.Any(ch=>!char.IsAsciiHexDigit(ch)||char.IsAsciiLetterLower(ch)))
            throw new InvalidDataException("Invalid canonical child fingerprint.");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(child.FileOrdinal!=f.ConfirmedOrdinal+1||p.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new ZipFenceException();
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==child.FileOrdinal).Plan;
        if(entry.IsEncrypted||entry.IsDirectory||entry.ExpandedLength!=child.Length||entry.Crc32!=child.Crc32||child.PlanHash!=p.Fence.PlanHash)throw new InvalidDataException("ZIP integrity proof disagrees with plan.");
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$file",child.FileOrdinal);q.Parameters.AddWithValue("$reservation",child.ReservationId);
        q.CommandText="SELECT ReservationJson FROM ZipExtractionReservations WHERE ParentOperationId=$op AND ReservationId=$reservation";
        var reservation=Decode<ZipWorkReservation>((string?)await q.ExecuteScalarAsync(ct)??throw new InvalidDataException("Missing extraction reservation."));
        if(reservation.Kind!=ZipWorkKind.Extraction||reservation.FileOrdinal!=child.FileOrdinal)throw new InvalidDataException("Wrong integrity attempt binding.");
        q.CommandText="SELECT ChargeJson FROM ZipExtractionCharges WHERE ParentOperationId=$op AND ReservationId=$reservation";
        long bytes=0;using(var rows=await q.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))bytes=checked(bytes+Decode<ZipExtractionCharge>(rows.GetString(0)).ExpandedBytes);
        if(bytes!=child.Length)throw new InvalidDataException("Integrity verification has uncharged payload work.");
        q.CommandText="SELECT VerificationJson FROM ZipExtractionVerifications WHERE ParentOperationId=$op AND FileOrdinal=$file";
        if(await q.ExecuteScalarAsync(ct) is string old)
        {var proof=Decode<ZipVerifiedChild>(old);if(proof with{ReservationId=child.ReservationId}!=child)throw new InvalidDataException("ZIP verified child changed across replay.");return p;}
        q.Parameters.AddWithValue("$json",Json(child));q.CommandText="INSERT INTO ZipExtractionVerifications VALUES($op,$file,$json)";await q.ExecuteNonQueryAsync(ct);
        p=p with{Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);tx.Commit();return p;
    }
    public async Task<ZipParentSnapshot> MaterializedAsync(ZipFence f,int ordinal,ZipRetentionRecord retention,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(p.State is not (ZipParentState.Planned or ZipParentState.Processing)||ordinal!=f.ConfirmedOrdinal+1)throw new ZipFenceException();
        var r=await RetentionAsync(c,tx,f.OperationId,ordinal,ct);
        if(r is null||Json(r)!=Json(retention)||r.State!=ZipRetentionState.Created)throw new InvalidDataException("ZIP materialization has no exact protected retention.");
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);
        using(var proofQuery=c.CreateCommand())
        {
            proofQuery.Transaction=tx;proofQuery.CommandText="SELECT VerificationJson FROM ZipExtractionVerifications WHERE ParentOperationId=$op AND FileOrdinal=$file";
            proofQuery.Parameters.AddWithValue("$op",f.OperationId);proofQuery.Parameters.AddWithValue("$file",ordinal);
            var proof=Decode<ZipVerifiedChild>((string?)await proofQuery.ExecuteScalarAsync(ct)??throw new InvalidDataException("ZIP materialization has no durable integrity proof."));
            if(proof.FileOrdinal!=ordinal||proof.Crc32!=entry.Plan.Crc32||proof.Length!=r.Identity.Length||proof.Sha256!=r.Identity.PlaintextSha256||proof.PlanHash!=p.Fence.PlanHash)throw new InvalidDataException("ZIP integrity and retention bindings disagree.");
        }
        if(entry.State is ZipEntryState.Materialized or ZipEntryState.Scanned or ZipEntryState.Ingested or ZipEntryState.Acknowledged or ZipEntryState.Released)
        {
            if(entry.Retained!=new ZipRetainedBinding(r.Identity.ObjectId,r.CreateReceipt!.CurrentRevision!.Value.Value,r.Identity.PlaintextSha256,r.Identity.Length))
                throw new InvalidDataException("ZIP retained progress changed.");
            return p;
        }
        if(entry.State!=ZipEntryState.Planned||entry.Plan.IsEncrypted||entry.Plan.ExpandedLength!=r.Identity.Length)throw new InvalidDataException("ZIP child is not eligible for materialization.");
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";
        q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$file",ordinal);
        q.Parameters.AddWithValue("$progress",Json(entry with{State=ZipEntryState.Materialized,Retained=new(r.Identity.ObjectId,r.CreateReceipt!.CurrentRevision!.Value.Value,r.Identity.PlaintextSha256,r.Identity.Length)}));await q.ExecuteNonQueryAsync(ct);
        p=p with{State=ZipParentState.Processing,Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);tx.Commit();return (await ReadAsync(f.OperationId,ct))!;
    }
    public async Task<ZipParentSnapshot> RejectExtractionAsync(ZipFence f,int ordinal,string reason,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>128||reason.Any(char.IsControl))throw new ArgumentException("Safe reason required.");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(ordinal!=f.ConfirmedOrdinal+1||p.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new ZipFenceException();
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";
        q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$file",ordinal);q.Parameters.AddWithValue("$progress",Json(entry with{State=ZipEntryState.RequiresReview,SafeReason=reason}));await q.ExecuteNonQueryAsync(ct);
        p=p with{State=ZipParentState.RequiresReview,Budget=p.Budget with{RejectedEntries=checked(p.Budget.RejectedEntries+1)},Fence=f with{Revision=checked(f.Revision+1)}};ValidateBudget(p.Budget);await SaveAsync(c,tx,p,ct);tx.Commit();return (await ReadAsync(f.OperationId,ct))!;
    }
}
