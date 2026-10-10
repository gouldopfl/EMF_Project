using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Contracts;

// Provider-neutral authenticated boundary. No stock implementation exists.
// Request/actor/classification must originate with a genuine issuer, not CLI,
// metadata, environment labels, or ownership tokens. Implementations must resolve
// the same request/approval after crashes and revalidate before each effect grant.
public interface IZipParentOperationHost
{
    Task<ZipParentAdmission> ResolveAsync(CancellationToken ct = default);
    Task<ZipParentAdmission> ApproveAsync(ZipParentAdmission pending, CancellationToken ct = default);
    Task<IZipAuthorizedExecution> AuthorizeRecoveryAsync(ZipParentAdmission admission, CancellationToken ct = default);
    Task<bool> AuthorizeReviewAsync(ZipParentAdmission admission, string safeReason, CancellationToken ct = default);
}

// A grant is distinct from a concurrency lease. The issuer defines expiry and
// revocation visibility; disposal ends authority to start further effects.
public interface IZipAuthorizedExecution : IAsyncDisposable
{
    ZipParentAdmission Admission { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
    CancellationToken Revoked { get; }
    Task RevalidateAsync(CancellationToken ct = default);
    Task<ZipChildIngestionRuntime> OpenChildAsync(ZipParentSnapshot parent, ZipEntryProgress entry, CancellationToken ct = default);
}
