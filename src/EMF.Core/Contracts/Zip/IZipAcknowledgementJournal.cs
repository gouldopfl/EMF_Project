namespace EMF.Core.Contracts.Zip;
public sealed record ZipRelationshipOccurrence(string ParentOperationId,string ParentArtifactId,string PlanHash,
    int CentralOrdinal,int FileOrdinal,string ChildOperationId,string ProvisionalArtifactId,string CanonicalArtifactId,string ChildSha256,int ZipOccurrenceVersion=1);
public sealed record ZipAcknowledgementDetails(int CentralOrdinal,string ProvisionalArtifactId,ZipRetainedBinding ParentInput,
    ZipRetainedBinding ChildInput,ZipChildIngestionProof Ingestion,ZipFence AdmittedFence,ZipFence CommittedFence,DateTimeOffset CreatedUtc);
public sealed record ZipAcknowledgementUpdate(ZipParentSnapshot Parent,ZipAcknowledgementBinding Acknowledgement,
    long ContainsId,long DerivedFromId,bool AlreadyAcknowledged);
public interface IZipAcknowledgementJournal
{
    Task<ZipAcknowledgementUpdate> AcknowledgeEntryAsync(ZipFence currentFence,int fileOrdinal,ZipChildIngestionProof expectedIngestion,CancellationToken ct=default);
}
