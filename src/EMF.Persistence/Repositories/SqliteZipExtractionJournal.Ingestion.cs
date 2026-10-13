using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using Microsoft.Data.Sqlite;
namespace EMF.Persistence.Repositories;
public sealed partial class SqliteZipExtractionJournal
{
    public async Task<bool> HasIngestionReservationAsync(ZipFence fence,int ordinal,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:true);
        var parent=await FencedAsync(c,tx,fence,ct);
        if(ordinal!=fence.ConfirmedOrdinal+1 || parent.Entries.All(e=>e.Plan.FileOrdinal!=ordinal))
            throw new ZipFenceException();
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=$kind";
        q.Parameters.AddWithValue("$op",fence.OperationId);
        q.Parameters.AddWithValue("$file",ordinal);
        q.Parameters.AddWithValue("$kind",(int)ZipWorkKind.Ingestion);
        return Convert.ToInt32(await q.ExecuteScalarAsync(ct))>0;
    }
    private static async Task ValidateIngestionProofAsync(SqliteConnection c,SqliteTransaction? tx,ZipParentSnapshot p,ZipEntryProgress e,ZipChildIngestionProof proof,CancellationToken ct)
    {
        if(proof.ChildOperationId!=e.Plan.ChildOperationId||proof.ProvisionalArtifactId!=e.Plan.ProvisionalArtifactId||proof.Sha256!=e.Retained?.Sha256||
            proof.Disposition is not (ArtifactIngestionDisposition.ProvisionalArtifactAdopted or ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact))throw new InvalidDataException("ZIP ingestion proof binding changed.");
        ArtifactIngestionIntent accepted;IngestionOperationBinding original;
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",proof.ChildOperationId);
        q.CommandText="SELECT i.IntentJson,i.Revision,i.State,o.BindingJson,d.DraftJson,i.ArtifactId,i.OwnershipToken,o.BaselineJson,o.ArtifactId,cc.ReceiptJson,cb.BindingJson FROM ArtifactIngestionIntents i JOIN ArtifactIngestionOperations o ON o.OperationId=i.OperationId JOIN ArtifactIngestionDrafts d ON d.OperationId=i.OperationId LEFT JOIN ArtifactIngestionCandidateCreations cc ON cc.OperationId=i.OperationId LEFT JOIN ArtifactIngestionCandidates cb ON cb.OperationId=i.OperationId WHERE i.OperationId=$op";
        using(var row=await q.ExecuteReaderAsync(ct))
        {
            if(!await row.ReadAsync(ct))throw new InvalidDataException("Missing original ingestion evidence.");
            var intent=Decode<ArtifactIngestionIntent>(row.GetString(0));var binding=Decode<IngestionOperationBinding>(row.GetString(3));var draft=Decode<IngestionMetadataDraft>(row.GetString(4));
            var baseline=Decode<ArtifactIngestionIntent>(row.GetString(7));
            accepted=intent;original=binding;
            if(row.GetString(8)!=intent.ArtifactId.Value||row.GetString(5)!=intent.ArtifactId.Value||row.GetString(6)!=intent.OwnershipToken.Value||
                baseline.OperationId!=intent.OperationId||baseline.ArtifactId!=intent.ArtifactId||baseline.OwnershipToken!=intent.OwnershipToken||
                baseline.ClassificationId!=intent.ClassificationId||baseline.ClassificationRevision!=intent.ClassificationRevision||baseline.AuthorizedOperationId!=intent.AuthorizedOperationId||
                baseline.CleanupOperationId!=intent.CleanupOperationId||baseline.CreateEventId!=intent.CreateEventId||baseline.AdoptionEventId!=intent.AdoptionEventId||baseline.CleanupEventId!=intent.CleanupEventId||baseline.PreparedUtc!=intent.PreparedUtc||
                intent.OperationId.Value!=proof.ChildOperationId||intent.ArtifactId.Value!=proof.ProvisionalArtifactId||intent.CanonicalArtifactId?.Value!=proof.CanonicalArtifactId||
                intent.Disposition!=proof.Disposition||intent.Revision<proof.IngestionRevision||row.GetInt64(1)!=intent.Revision||row.GetInt32(2)!=(int)intent.State||
                (proof.Disposition==ArtifactIngestionDisposition.ProvisionalArtifactAdopted?intent.State!=ArtifactIngestionState.Completed:intent.State is not (ArtifactIngestionState.CleanupClaimed or ArtifactIngestionState.Cleaned))||
                intent.ClassificationId.Value!=proof.ClassificationId||binding.OperationId!=intent.AuthorizedOperationId||binding.ParentOperationId?.Value!=p.Binding.OperationId||
                draft.Artifact.Id.Value!=proof.ProvisionalArtifactId||draft.Provenance.ArtifactId!=draft.Artifact.Id||draft.Artifact.Fingerprint?.Algorithm!="SHA-256"||draft.Artifact.Fingerprint.Value!=proof.Sha256||intent.CreateReceipt is null)
                throw new InvalidDataException("ZIP outcome contradicts original durable ingestion.");
            var receipt=intent.CreateReceipt!;
            if(receipt.OperationId!=intent.OperationId||receipt.ArtifactId!=intent.ArtifactId||receipt.OwnershipToken!=intent.OwnershipToken||receipt.Kind!=ArtifactContentMutationKind.Create||receipt.Outcome!=ArtifactContentMutationOutcome.Created||receipt.PriorRevision is not null||receipt.CurrentRevision is null||receipt.AuditEventId!=intent.CreateEventId||receipt.AuditObligationVersion!=1||row.IsDBNull(9)||Decode<ArtifactContentMutationReceipt>(row.GetString(9))!=receipt||row.IsDBNull(10))throw new InvalidDataException("ZIP ingestion creation receipt contradicts immutable evidence.");
            var candidate=Decode<IngestionCandidateBinding>(row.GetString(10));
            if(candidate.OperationId!=intent.OperationId||candidate.ArtifactId!=intent.ArtifactId||candidate.CandidateHash!=intent.CandidateHash||candidate.OwnershipToken!=intent.OwnershipToken||candidate.AuthorizedOperationId!=intent.AuthorizedOperationId||candidate.ParentOperationId!=binding.ParentOperationId||candidate.ClassificationId!=intent.ClassificationId||candidate.ClassificationRevision!=intent.ClassificationRevision||candidate.CreateEventId!=intent.CreateEventId||candidate.AdoptionEventId!=intent.AdoptionEventId||candidate.CleanupEventId!=intent.CleanupEventId)throw new InvalidDataException("ZIP ingestion candidate binding changed.");
        }
        q.Parameters.AddWithValue("$id",proof.CanonicalArtifactId);
        q.CommandText="SELECT a.FingerprintAlgorithm,a.FingerprintValue,m.ClassificationId,m.ClassificationRevision,m.IsAdopted,s.OperationId FROM Artifacts a JOIN ArtifactMutationAuthority m ON m.ArtifactId=a.Id JOIN ArtifactIngestionAdoptions s ON s.ArtifactId=a.Id WHERE a.Id=$id AND EXISTS(SELECT 1 FROM Provenance WHERE ArtifactId=a.Id)";
        using(var row=await q.ExecuteReaderAsync(ct))
        {
            if(!await row.ReadAsync(ct)||row.GetString(0)!="SHA-256"||row.GetString(1)!=proof.Sha256||row.GetString(2)!=proof.ClassificationId||row.GetString(3)!=proof.ClassificationRevision||row.GetInt32(4)!=1||
                (proof.Disposition==ArtifactIngestionDisposition.ProvisionalArtifactAdopted&&(proof.CanonicalArtifactId!=proof.ProvisionalArtifactId||row.GetString(5)!=proof.ChildOperationId)))
                throw new InvalidDataException("Missing canonical adopted authority.");
        }
        q.CommandText="SELECT COUNT(*) FROM ArtifactIngestionReviews WHERE OperationId=$op";
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0)throw new InvalidDataException("ZIP child has contradictory review evidence.");
        var expected=new[]{(accepted.CreateEventId,IngestionAuditAction.Created),(accepted.AdoptionEventId,proof.Disposition==ArtifactIngestionDisposition.ProvisionalArtifactAdopted?IngestionAuditAction.Adopted:IngestionAuditAction.Deduplicated)}.ToList();
        if(proof.Disposition==ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact&&accepted.State==ArtifactIngestionState.Cleaned)expected.Add((accepted.CleanupEventId,IngestionAuditAction.Deleted));
        foreach(var (id,action) in expected)
        {
            q.Parameters.Clear();q.Parameters.AddWithValue("$id",id.Value);q.Parameters.AddWithValue("$op",proof.ChildOperationId);
            q.CommandText="SELECT f.ObligationJson,a.ObligationJson,a.Delivered FROM ArtifactIngestionAuditFacts f JOIN ArtifactIngestionAudit a ON a.EventId=f.EventId AND a.OperationId=f.OperationId WHERE f.EventId=$id AND f.OperationId=$op";
            using var row=await q.ExecuteReaderAsync(ct);
            if(!await row.ReadAsync(ct)||row.GetString(0)!=row.GetString(1)||row.GetInt32(2)!=1)throw new InvalidDataException("ZIP ingestion audit facts/delivery binding are missing.");
            var fact=Decode<IngestionAuditObligation>(row.GetString(0));
            if(fact.EventId!=id||fact.OperationId!=accepted.OperationId||fact.ArtifactId!=accepted.ArtifactId||fact.ClassificationId!=accepted.ClassificationId||fact.ClassificationRevision.Value!=(action==IngestionAuditAction.Adopted?proof.ClassificationRevision:accepted.ClassificationRevision.Value)||fact.OriginalActorId!=original.OriginalActorId||fact.Action!=action)
                throw new InvalidDataException("ZIP ingestion audit identity contradicts its operation.");
        }
        // Delivered is only a local hint. Canonical sink acknowledgements are checked
        // by coordinator IngestAsync/ResumeAsync before the host submits this proof.
    }
    public Task<ZipParentSnapshot> RecordIngestionAsync(ZipFence f,int ordinal,ZipChildIngestionProof proof,CancellationToken ct=default)
        => RecordIngestionCoreAsync(f,ordinal,proof,null,ct);
    public Task<ZipParentSnapshot> RecordIngestionAsync(ZipFence f,int ordinal,ZipChildIngestionProof proof,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => RecordIngestionCoreAsync(f,ordinal,proof,guard??throw new ArgumentNullException(nameof(guard)),ct);
    private async Task<ZipParentSnapshot> RecordIngestionCoreAsync(ZipFence f,int ordinal,ZipChildIngestionProof proof,IZipEffectBoundaryGuard? guard,CancellationToken ct)
    {
        using var effectCancellation=guard is null?null:CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);
        ct=effectCancellation?.Token??ct;
        await ValidateEffectAsync(guard,ct);
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(guard is not null)await ValidateExecutionAdmissionAsync(c,tx,p,ct);
        if(ordinal!=f.ConfirmedOrdinal+1||p.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new ZipFenceException();
        var e=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);
        if(e.State==ZipEntryState.Ingested&&e.Ingestion==proof){await ValidateEffectAsync(guard,ct);return p;}
        if(e.State!=ZipEntryState.Scanned||e.Ingestion is not null)throw new InvalidDataException("ZIP ingestion acknowledgement is not eligible.");
        var scan=System.Text.Json.JsonSerializer.Deserialize<EMF.Core.Contracts.Malware.MalwareScanEvidence>(e.EvidenceJson??"")
            ??throw new InvalidDataException("Missing exact Clean scan evidence.");
        EMF.Core.Contracts.Malware.MalwareScanValidation.Evidence(scan);
        if(!scan.IsClean||e.Retained is null||scan.Request.Content!=new EMF.Core.Contracts.Malware.MalwareScanContent(
            e.Retained.ContentId,e.Retained.Revision,e.Retained.Sha256,e.Retained.Length))
            throw new InvalidDataException("Ingestion progression requires exact retained Clean evidence.");
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$file",ordinal);
        q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=3";
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))==0)throw new InvalidDataException("ZIP ingestion has no durable attempt.");
        await ValidateIngestionProofAsync(c,tx,p,e,proof,ct);
        q.Parameters.AddWithValue("$progress",Json(e with{State=ZipEntryState.Ingested,Ingestion=proof}));q.CommandText="UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";await q.ExecuteNonQueryAsync(ct);
        p=p with{Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);
        await CommitEffectAsync(tx,guard,ct,p.OwnerUntil,admissionEligibility:guard is null?null:()=>ValidateExecutionAdmissionAsync(c,tx,p,ct));
        return (await ReadAsync(f.OperationId,ct))!;
    }
}
