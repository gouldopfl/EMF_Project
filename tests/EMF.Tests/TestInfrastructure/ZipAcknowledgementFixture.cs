using System.Security.Cryptography;
using EMF.ConsoleApplication;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Security.Ingestion;
namespace EMF.Tests.TestInfrastructure;
internal static class ZipAcknowledgementFixture
{
    // Earlier retention-only tests use a synthetic admitted plan, not an actual ZIP.
    // Supply its explicitly trusted materialization boundary via real journal APIs;
    // this fixture does not claim to test decompression or CRC computation.
    internal static async Task<ZipParentSnapshot> MaterializationBoundary(ArtifactIngestionFixture evidence,SqliteZipExtractionJournal journal,ZipParentSnapshot parent,ReadOnlyMemory<byte> plaintext)
    {
        var ordinal=parent.Fence.ConfirmedOrdinal+1;var e=parent.Entries.Single(e=>e.Plan.FileOrdinal==ordinal);var reservation=Guid.NewGuid().ToString("N");
        parent=await journal.ReserveAsync(parent.Fence,new(reservation,ZipWorkKind.Extraction,ordinal,e.Plan.ExpandedLength,e.Plan.CompressedLength));
        parent=await journal.ChargeAsync(parent.Fence,new(Guid.NewGuid().ToString("N"),reservation,ordinal,ExpandedBytes:plaintext.Length,CrcBytes:plaintext.Length));
        parent=await journal.VerifyChildAsync(parent.Fence,new(reservation,ordinal,plaintext.Length,e.Plan.Crc32,Convert.ToHexString(SHA256.HashData(plaintext.Span)),parent.Fence.PlanHash!));
        return await journal.MaterializedAsync(parent.Fence,ordinal,(await journal.ReadRetentionAsync(parent.Binding.OperationId,ordinal))!);
    }
    internal static async Task<ZipParentSnapshot> Ingest(ArtifactIngestionFixture evidence,SqliteZipExtractionJournal journal,ZipParentSnapshot parent,ZipProtectedRetentionService retention)
    {
        parent=await new ZipChildScanService(journal,journal,journal,retention,new ZipChildScanTests.Scanner()).ScanNextAsync(parent);
        var e=parent.Entries.Single(e=>e.Plan.FileOrdinal==parent.Fence.ConfirmedOrdinal+1);
        evidence.SecurityContext.Operation=new(new(e.Plan.ChildOperationId!),evidence.SecurityContext.Operation.AuthorizedOperationId,evidence.SecurityContext.Operation.ActorId,new(parent.Binding.OperationId));
        var host=new ZipChildIngestionHost(evidence.DatabasePath,evidence.Physical,evidence.Staging,evidence.Encryption,
            (_,ct)=>Task.FromResult<IArtifactIngestionSecurityContext>(evidence.SecurityContext),evidence.Classification,evidence.Authorization,evidence.Audit,evidence.Fingerprints);
        return await new ZipChildIngestionService(journal,journal,journal,retention,host).IngestNextAsync(parent);
    }
}
