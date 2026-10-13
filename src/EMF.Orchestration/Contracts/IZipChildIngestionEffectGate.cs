using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Contracts;

// A request is bound to one current occurrence and one operation. Neither a
// reservation nor a previous ingestion receipt can stand in for this grant.
public enum ZipChildIngestionEffect
{
    Reservation, RetainedRead, OpenChild, Metadata, CandidateRead, CandidateStage,
    Encrypt, Decrypt, PhysicalRead, PhysicalPrepare, PhysicalCreate,
    Adoption, AuditRead, AuditWrite, Proof
}

public sealed record ZipChildIngestionEffectRequest(ZipParentAdmission Admission,
    ZipParentSnapshot Parent, ZipEntryPlan Occurrence, ZipChildIngestionEffect Effect);

// Scoped authority is issued anew for each provider or transaction boundary.
// Disposal never grants another effect. The implementation is tied to one root.
public interface IZipChildIngestionEffectGate
{
    TimeProvider Clock { get; }
    Task<IZipChildIngestionEffectScope> OpenAsync(ZipChildIngestionEffect effect,
        CancellationToken ct = default);
}

public interface IZipChildIngestionEffectScope : IZipEffectBoundaryGuard, IAsyncDisposable
{
    Task FinishAsync(CancellationToken ct = default);
}
