namespace EMF.Core.Contracts.Zip;

// Charges are append-only upper bounds committed before the corresponding work.
// Actual completion cannot refund a short read, cancellation or interrupted process.
public sealed record ZipExtractionCharge(string Id,string ReservationId,int FileOrdinal,
    long ExpandedBytes=0,long ParentReadBytes=0,long CrcBytes=0);
public sealed record ZipVerifiedChild(string ReservationId,int FileOrdinal,long Length,uint Crc32,string Sha256,string PlanHash);
public interface IZipExtractionExecutionJournal
{
    Task<ZipParentSnapshot> ChargeAsync(ZipFence fence,ZipExtractionCharge charge,CancellationToken ct=default);
    Task<ZipParentSnapshot> VerifyChildAsync(ZipFence fence,ZipVerifiedChild child,CancellationToken ct=default);
    Task<ZipParentSnapshot> MaterializedAsync(ZipFence fence,int fileOrdinal,ZipRetentionRecord retention,CancellationToken ct=default);
    Task<ZipParentSnapshot> RejectExtractionAsync(ZipFence fence,int fileOrdinal,string safeReason,CancellationToken ct=default);
}
