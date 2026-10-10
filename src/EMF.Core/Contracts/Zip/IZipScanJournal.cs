using EMF.Core.Contracts.Malware;
namespace EMF.Core.Contracts.Zip;
public sealed record ZipScanAttempt(string ReservationId,int FileOrdinal,string PlanHash,MalwareScanRequest Request);
public sealed record ZipScanUpdate(ZipParentSnapshot Parent,ZipScanAttempt Attempt);
public interface IZipScanJournal
{
    Task<ZipScanUpdate> BeginScanAsync(ZipFence fence,int fileOrdinal,MalwareScannerPolicy policy,CancellationToken ct=default);
    Task<ZipParentSnapshot> CompleteScanAsync(ZipFence fence,ZipScanAttempt attempt,MalwareScanEvidence evidence,CancellationToken ct=default);
}
