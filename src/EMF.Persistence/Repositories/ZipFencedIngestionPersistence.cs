using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
namespace EMF.Persistence.Repositories;

// Both concrete providers use this same database. The generic BEGIN IMMEDIATE writer
// excludes ownership/scan changes while the read-only ZIP check and session execute.
public sealed class ZipFencedIngestionPersistence : IArtifactIngestionPersistence
{
    private readonly SqliteArtifactIngestionPersistence _inner;
    private readonly SqliteZipExtractionJournal _parents;
    private readonly ZipFence _fence;
    private readonly ZipEntryProgress _entry;
    public ZipFencedIngestionPersistence(string evidenceDatabasePath,ZipFence fence,ZipEntryProgress entry)
    {var path=Path.GetFullPath(evidenceDatabasePath);_inner=new(path);_parents=new(path);_fence=fence;_entry=entry;}
    private void Operation(ArtifactContentOperationId id)
    {if(id.Value!=_entry.Plan.ChildOperationId)throw new InvalidDataException("ZIP ingestion operation changed.");}
    public Task<IDisposable> AcquireExecutionAsync(ArtifactContentOperationId id,CancellationToken ct=default)
    {Operation(id);return _inner.AcquireExecutionAsync(id,ct);}
    public async Task<IArtifactIngestionSession> AcquireAsync(ArtifactContentOperationId id,CancellationToken ct=default)
    {
        Operation(id);var session=await _inner.AcquireAsync(id,ct);
        try
        {
            var parent=await _parents.ReadAsync(_fence.OperationId,ct);
            if(parent?.Fence!=_fence||parent.OwnerUntil<=DateTimeOffset.UtcNow||parent.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new ZipFenceException();
            if(_entry.Plan.FileOrdinal!=_fence.ConfirmedOrdinal+1)throw new ZipFenceException();
            var current=parent.Entries.Single(e=>e.Plan.FileOrdinal==_entry.Plan.FileOrdinal);
            if(current.Plan!=_entry.Plan||current.Retained!=_entry.Retained||current.EvidenceJson!=_entry.EvidenceJson||current.State is not (ZipEntryState.Scanned or ZipEntryState.Ingested))
                throw new InvalidDataException("ZIP ingestion admission changed.");
            var scan=System.Text.Json.JsonSerializer.Deserialize<MalwareScanEvidence>(current.EvidenceJson!)??throw new InvalidDataException("Missing scan authority.");
            MalwareScanValidation.Evidence(scan);if(!scan.IsClean)throw new InvalidDataException("ZIP ingestion requires bound Clean evidence.");
            if(session.ProvisionalArtifactId is {} provisional&&provisional.Value!=current.Plan.ProvisionalArtifactId)throw new InvalidDataException("ZIP provisional identity changed.");
            return session;
        }
        catch{await session.DisposeAsync();throw;}
    }
    public Task<IReadOnlyList<IngestionRecoveryWork>> ReadRecoveryWorkAsync(long after,int limit,CancellationToken ct=default)=>_inner.ReadRecoveryWorkAsync(after,limit,ct);
    public Task<IReadOnlyList<IngestionAuditObligation>> ReadAuditObligationsAsync(ArtifactContentOperationId id,CancellationToken ct=default)=>_inner.ReadAuditObligationsAsync(id,ct);
    public Task<IngestionAuditObligation?> ReadReceiptAuditObligationAsync(ArtifactContentMutationReceipt r,CancellationToken ct=default)=>_inner.ReadReceiptAuditObligationAsync(r,ct);
    public Task<IngestionAuditObligation?> ReadReviewAuditObligationAsync(ArtifactContentOperationId id,CancellationToken ct=default)=>_inner.ReadReviewAuditObligationAsync(id,ct);
    public Task AcknowledgeAuditAsync(IngestionAuditObligation o,CancellationToken ct=default)=>_inner.AcknowledgeAuditAsync(o,ct);
    public Task MarkAuditPendingAsync(IngestionAuditObligation o,CancellationToken ct=default)=>_inner.MarkAuditPendingAsync(o,ct);
    public Task RecordOrphanReceiptAsync(ArtifactContentMutationReceipt r,CancellationToken ct=default)=>_inner.RecordOrphanReceiptAsync(r,ct);
    public Task<ArtifactContentOperationId?> FindReceiptOperationAsync(ArtifactContentMutationReceipt r,CancellationToken ct=default)=>_inner.FindReceiptOperationAsync(r,ct);
    public Task<IngestionRecoveryHealth> ReadRecoveryHealthAsync(CancellationToken ct=default)=>_inner.ReadRecoveryHealthAsync(ct);
}
