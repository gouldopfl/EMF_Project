using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
namespace EMF.Orchestration.Services;
public sealed class ZipChildIngestionService(IZipExtractionJournal parents,IZipChildIngestionJournal ingestions,
    IZipExtractionExecutionJournal failures,ZipProtectedRetentionService retention,IZipChildIngestionHost? host)
{
    internal Func<string,Task>? Checkpoint { get; set; }
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    public async Task<ZipParentSnapshot> IngestNextAsync(ZipParentSnapshot parent,CancellationToken ct=default)
    {
        await ZipChildWorkGate.Gate.WaitAsync(ct);
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(ZipNumericLimits.AttemptTimeout);var token=deadline.Token;
            var current=await parents.ReadAsync(parent.Binding.OperationId,token);if(current?.Fence!=parent.Fence)throw new ZipFenceException();parent=current;
            if(parent.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new InvalidOperationException("ZIP ingestion is not eligible.");
            var ordinal=checked(parent.Fence.ConfirmedOrdinal+1);var entry=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);
            if(entry is null)return parent;
            try
            {
                if(host is null)throw new InvalidDataException("ZIP child ingestion requires authoritative host composition.");
                if(entry.State is not (ZipEntryState.Scanned or ZipEntryState.Ingested)||entry.Retained is null)throw new InvalidDataException("ZIP child has no scan admission.");
                var scan=JsonSerializer.Deserialize<MalwareScanEvidence>(entry.EvidenceJson!)??throw new InvalidDataException("Missing authoritative ZIP scan.");MalwareScanValidation.Evidence(scan);
                if(!scan.IsClean||scan.Request.Content!=new MalwareScanContent(entry.Retained.ContentId,entry.Retained.Revision,entry.Retained.Sha256,entry.Retained.Length))throw new InvalidDataException("ZIP child requires exact bound Clean evidence.");
                if(entry.State==ZipEntryState.Ingested)
                {
                    var existing=await host.OpenAsync(parent,entry,token);await At("Resuming");
                    var resumed=await existing.Coordinator.ResumeAsync(token);
                    if(resumed.AuditDelivery!=IngestionAuditDelivery.Completed)throw new InvalidDataException("Recorded ZIP ingestion cannot reconcile audit authority.");
                    var proof=await ProofAsync(existing,entry,token);
                    if(entry.Ingestion is null||entry.Ingestion with{IngestionRevision=proof.IngestionRevision}!=proof||proof.IngestionRevision<entry.Ingestion.IngestionRevision)throw new InvalidDataException("ZIP ingestion recovery evidence changed.");return parent;
                }
                parent=await parents.ReserveAsync(parent.Fence,new(Guid.NewGuid().ToString("N"),ZipWorkKind.Ingestion,ordinal),token);await At("Reserved");
                var runtime=await host.OpenAsync(parent,entry,token);bool exists;
                await using(var session=await runtime.Persistence.AcquireAsync(new(entry.Plan.ChildOperationId!),token))
                {if(session.IsDamaged||session.HasReview)throw new InvalidDataException("Original ZIP child ingestion requires review.");exists=session.Intent is not null;}
                ArtifactIngestionOutcome outcome;
                if(exists){await At("Resuming");outcome=await runtime.Coordinator.ResumeAsync(token);}
                else
                {
                    await using var lease=await retention.OpenAsync(parent,ordinal,token);
                    var id=new ArtifactId(entry.Plan.ProvisionalArtifactId!);
                    var draft=new IngestionMetadataDraft(new Artifact{Id=id,Name=entry.Plan.DisplayName,ArtifactType="file",Fingerprint=new(){Algorithm="SHA-256",Value=entry.Retained.Sha256}},
                        new Provenance{ArtifactId=id,Source="zip-parent:"+parent.Binding.ParentArtifactId+"/entry:"+entry.Plan.CentralOrdinal,RecordedBy="zip-extraction"});
                    await At("Ingesting");outcome=await runtime.Coordinator.IngestAsync(draft,lease.Content,token);
                }
                await At("CoordinatorReturned");
                if(outcome.OperationId.Value!=entry.Plan.ChildOperationId||outcome.Disposition is not (ArtifactIngestionDisposition.ProvisionalArtifactAdopted or ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact))
                    throw new InvalidDataException("Original ZIP ingestion has no successful durable outcome.");
                if(outcome.AuditDelivery==IngestionAuditDelivery.RequiresReview)throw new InvalidDataException("ZIP ingestion audit requires review.");
                if(outcome.AuditDelivery!=IngestionAuditDelivery.Completed)throw new IOException("ZIP ingestion delivery remains pending.");
                var verified=await ProofAsync(runtime,entry,token);await At("OutcomeVerified");
                parent=await ingestions.RecordIngestionAsync(parent.Fence,ordinal,verified,token);await At("Recorded");return parent;
            }
            catch(Exception error) when(error is InvalidDataException or UnauthorizedAccessException or ArtifactIngestionReviewException or ArtifactContentIdempotencyException)
            {
                using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);var latest=await parents.ReadAsync(parent.Binding.OperationId,review.Token);
                if(latest?.Fence==parent.Fence&&latest.State is (ZipParentState.Planned or ZipParentState.Processing))await failures.RejectExtractionAsync(parent.Fence,ordinal,"ChildIngestionEvidenceRejected",review.Token);
                throw;
            }
        }
        finally{ZipChildWorkGate.Gate.Release();}
    }
    private static async Task<ZipChildIngestionProof> ProofAsync(ZipChildIngestionRuntime runtime,ZipEntryProgress entry,CancellationToken ct)
    {
        await using var s=await runtime.Persistence.AcquireAsync(new(entry.Plan.ChildOperationId!),ct);var i=s.Intent;
        if(s.IsDamaged||s.HasReview||i is null||i.ArtifactId.Value!=entry.Plan.ProvisionalArtifactId||i.CanonicalArtifactId is null||
            i.Disposition is not (ArtifactIngestionDisposition.ProvisionalArtifactAdopted or ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact)||
            (i.Disposition==ArtifactIngestionDisposition.ProvisionalArtifactAdopted?i.State!=ArtifactIngestionState.Completed:i.State is not (ArtifactIngestionState.CleanupClaimed or ArtifactIngestionState.Cleaned)))throw new InvalidDataException("Incomplete original ingestion outcome.");
        var result=await s.ReadResultAsync(ct);var authority=await s.ResolveAuthorityAsync(i.CanonicalArtifactId.Value,ct);var draft=await s.ReadDraftAsync(ct);
        if(result?.Artifact.Id!=i.CanonicalArtifactId||result.Artifact.Fingerprint?.Algorithm!="SHA-256"||result.Artifact.Fingerprint.Value!=entry.Retained!.Sha256||draft?.Artifact.Id!=i.ArtifactId||
            authority is not{IsAdopted:true}||authority.ClassificationId!=i.ClassificationId||!await s.HasAdoptionEvidenceAsync(i.CanonicalArtifactId.Value,ct))throw new InvalidDataException("Canonical ZIP child outcome lacks authority.");
        return new(entry.Plan.ChildOperationId!,entry.Plan.ProvisionalArtifactId!,i.CanonicalArtifactId.Value.Value,i.Disposition,entry.Retained.Sha256,authority.ClassificationId.Value,authority.Revision.Value,i.Revision);
    }
}
