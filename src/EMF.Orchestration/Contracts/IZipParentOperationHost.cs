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
    Task<IZipAuthorizedExecution> AuthorizeInitialExecutionAsync(ZipParentAdmission admission, CancellationToken ct = default);
    Task<IZipAuthorizedExecution> AuthorizeRecoveryAsync(ZipParentAdmission admission, CancellationToken ct = default);
    Task<IZipAuthorizedParentRetention> AuthorizeInitialRetentionAsync(ZipParentAdmission admission, CancellationToken ct = default)
        => Task.FromException<IZipAuthorizedParentRetention>(new NotSupportedException("Initial retention authority is unavailable."));
    Task<IZipAuthorizedParentRetention> AuthorizeRetentionRecoveryAsync(ZipParentAdmission admission, CancellationToken ct = default)
        => Task.FromException<IZipAuthorizedParentRetention>(new NotSupportedException("Retention recovery authority is unavailable."));
    Task<bool> AuthorizeReviewAsync(ZipParentAdmission admission, string safeReason, CancellationToken ct = default);
}

// A grant is distinct from a concurrency lease. The issuer defines expiry and
// revocation visibility; disposal ends authority to start further effects and
// cancels outstanding scopes. One transient grant per attempt; never serialize
// it or reconstruct it from durable approval. Restart requires a fresh grant.
// Admission, Purpose, ExecutingActorId and ValidUntilUtc are immutable throughout
// the attempt. Lease renewal cannot refresh or widen this live authority.
public interface IZipAuthorizedExecution : IAsyncDisposable
{
    ZipParentAdmission Admission { get; }
    ZipOwnershipPurpose Purpose { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
    CancellationToken Revoked { get; }
    Task RevalidateAsync(CancellationToken ct = default);
    Task<IZipAuthorizedOwnershipScope> AuthorizeOwnershipEffectAsync(ZipOwnershipClaimRequest request, CancellationToken ct = default);
    Task<IZipAuthorizedEffectScope<ZipParentReadRequest>> AuthorizeParentReadAsync(ZipParentReadRequest request, CancellationToken ct = default)
        => Task.FromException<IZipAuthorizedEffectScope<ZipParentReadRequest>>(new NotSupportedException("Parent read authority is unavailable."));
    Task<IZipAuthorizedEffectScope<ZipPlanAdmissionRequest>> AuthorizePlanAdmissionAsync(ZipPlanAdmissionRequest request, CancellationToken ct = default)
        => Task.FromException<IZipAuthorizedEffectScope<ZipPlanAdmissionRequest>>(new NotSupportedException("Plan authority is unavailable."));
    Task<IZipAuthorizedEffectScope<ZipChildExtractionRequest>> AuthorizeChildExtractionAsync(ZipChildExtractionRequest request, CancellationToken ct = default)
        => Task.FromException<IZipAuthorizedEffectScope<ZipChildExtractionRequest>>(new NotSupportedException("Extraction authority is unavailable."));
    Task<IZipAuthorizedEffectScope<ZipChildRetentionRequest>> AuthorizeChildRetentionAsync(ZipChildRetentionRequest request, CancellationToken ct = default)
        => Task.FromException<IZipAuthorizedEffectScope<ZipChildRetentionRequest>>(new NotSupportedException("Retention authority is unavailable."));
    Task<ZipChildIngestionRuntime> OpenChildAsync(ZipParentSnapshot parent, ZipEntryProgress entry, CancellationToken ct = default);
}
