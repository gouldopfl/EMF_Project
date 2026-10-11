using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Contracts;

// Transient, single-use capability for exactly Request, derived from Root.
// Actor, request and deadline are immutable; deadline cannot exceed the root's.
// ExecuteAsync must revalidate current issuer authority after waits and before
// admitting the callback, link caller/root/scope cancellation and absolute expiry,
// and reject disposal or reuse. Root disposal cancels all outstanding scopes.
// Unsupported effects fail closed; this grants no payload or lifecycle authority.
//
// Issuers must define how effect acceptance orders against external revocation.
// Cancellation can prevent effects only before their commit boundary; an already
// committed concurrency claim is not undone by post-commit invalidation. Neither
// this interface nor synthetic tests establishes atomic external revocation.
public interface IZipAuthorizedOwnershipScope : IAsyncDisposable
{
    IZipAuthorizedExecution Root { get; }
    ZipOwnershipClaimRequest Request { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
    CancellationToken Revoked { get; }
    Task<ZipParentSnapshot> ExecuteAsync(Func<CancellationToken, Task<ZipParentSnapshot>> effect,
        CancellationToken ct = default);
}
