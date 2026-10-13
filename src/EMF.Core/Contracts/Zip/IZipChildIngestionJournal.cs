using EMF.Core.Contracts.Ingestion;
namespace EMF.Core.Contracts.Zip;
public sealed record ZipChildIngestionProof(string ChildOperationId,string ProvisionalArtifactId,string CanonicalArtifactId,
    ArtifactIngestionDisposition Disposition,string Sha256,string ClassificationId,string ClassificationRevision,long IngestionRevision);
public interface IZipChildIngestionJournal
{
    Task<bool> HasIngestionReservationAsync(ZipFence fence,int ordinal,CancellationToken ct=default)
        => throw new NotSupportedException("Durable ZIP child ingestion reservation lookup is required.");
    Task<ZipParentSnapshot> RecordIngestionAsync(ZipFence fence,int ordinal,ZipChildIngestionProof proof,CancellationToken ct=default);
    Task<ZipParentSnapshot> RecordIngestionAsync(ZipFence fence,int ordinal,ZipChildIngestionProof proof,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
        => throw new NotSupportedException("Guarded ZIP ingestion proof progression is required.");
}
