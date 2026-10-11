using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Contracts;

// Each request is transient. Its full projection is the expected input to the
// effect, not a stored permission and not a replacement for journal eligibility.
public sealed record ZipParentReadRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent);
public sealed record ZipPlanAdmissionRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent);
public sealed record ZipChildExtractionRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent, ZipEntryPlan Occurrence);
public sealed record ZipChildRetentionRequest(ZipParentAdmission Admission, ZipParentSnapshot Parent,
    ZipEntryPlan Occurrence, ZipRetentionRecord? Retention, string NamespaceId);
public sealed record ZipInitialRetentionRequest(ZipParentAdmission Admission, ZipRetainedBinding? Source,
    ZipParentRetentionRecord? Retention, string NamespaceId);

// The provider orders local acceptance against revocation. It must validate
// its own root at Accept, without an await between acceptance and the effect.
// Accepted effects may complete; this never permits the next effect.
// Invalidated signals authority revocation/expiry, including during asynchronous
// disposal. Normal completion/disposal rejects future Accept but does not signal
// Invalidated; otherwise a caller cannot distinguish closure from revocation.
// Disposal must finish outstanding work before returning.
public interface IZipAuthorizedEffectScope<out TRequest> : IZipEffectBoundaryGuard, IAsyncDisposable
{
    object Root { get; }
    TRequest Request { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
}

// Separate pre-ParentBound authority. It can produce SourceBound/ParentBound
// evidence, but cannot execute a plan, claim ownership or open a child runtime.
public interface IZipAuthorizedParentRetention : IAsyncDisposable
{
    ZipParentAdmission Admission { get; }
    string ExecutingActorId { get; }
    DateTimeOffset ValidUntilUtc { get; }
    CancellationToken Revoked { get; }
    Task RevalidateAsync(CancellationToken ct = default);
    Task<IZipAuthorizedEffectScope<ZipInitialRetentionRequest>> AuthorizeRetentionEffectAsync(
        ZipInitialRetentionRequest request, CancellationToken ct = default);
}
