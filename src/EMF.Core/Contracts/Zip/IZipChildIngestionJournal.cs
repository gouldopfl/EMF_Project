using EMF.Core.Contracts.Ingestion;
namespace EMF.Core.Contracts.Zip;
public sealed record ZipChildIngestionProof(string ChildOperationId,string ProvisionalArtifactId,string CanonicalArtifactId,
    ArtifactIngestionDisposition Disposition,string Sha256,string ClassificationId,string ClassificationRevision,long IngestionRevision);
public interface IZipChildIngestionJournal
{
    Task<ZipParentSnapshot> RecordIngestionAsync(ZipFence fence,int ordinal,ZipChildIngestionProof proof,CancellationToken ct=default);
}
