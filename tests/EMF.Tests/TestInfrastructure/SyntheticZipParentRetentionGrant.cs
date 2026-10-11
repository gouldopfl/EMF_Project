using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Tests.TestInfrastructure;

internal sealed class SyntheticZipParentRetentionGrant(ZipParentAdmission admission, TimeProvider clock)
    : IZipAuthorizedParentRetention
{
    private readonly CancellationTokenSource revoked = new();
    private bool disposed;
    public ZipParentAdmission Admission { get; } = admission;
    public string ExecutingActorId { get; } = admission.Binding.OriginalActorId;
    public DateTimeOffset ValidUntilUtc { get; } = clock.GetUtcNow().AddMinutes(10);
    public CancellationToken Revoked => revoked.Token;
    internal Func<ZipInitialRetentionRequest, Task>? OnEffectRequest {get;set;}
    internal Func<Task>? BeforeDisposal;
    internal List<ZipInitialRetentionRequest> Requests { get; } = [];
    internal void Revoke() => revoked.Cancel();
    private void Check(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Revoked.ThrowIfCancellationRequested();
        if (disposed || clock.GetUtcNow() >= ValidUntilUtc) throw new UnauthorizedAccessException("Synthetic retention root unavailable.");
    }
    public Task RevalidateAsync(CancellationToken ct = default) { Check(ct); return Task.CompletedTask; }
    public async Task<IZipAuthorizedEffectScope<ZipInitialRetentionRequest>> AuthorizeRetentionEffectAsync(
        ZipInitialRetentionRequest request, CancellationToken ct = default)
    {
        Check(ct);
        if (request.Admission.OperationId != Admission.OperationId || request.Admission.Key != Admission.Key ||
            request.Admission.Binding != Admission.Binding || request.NamespaceId != Admission.Binding.ParentNamespaceId ||
            request.Source is { } source && (source.ContentId != Admission.Binding.SourceContentId || source.Revision != Admission.Binding.SourceRevision) ||
            request.Retention is { } retention && (retention.Identity.ParentOperationId != Admission.OperationId ||
                retention.Identity.NamespaceId != Admission.Binding.ParentNamespaceId || retention.Identity.ProfileHash != Admission.Binding.ProfileHash))
            throw new UnauthorizedAccessException("Synthetic retention scope resource mismatch.");
        Requests.Add(request);
        if (OnEffectRequest is not null) await OnEffectRequest(request);
        Check(ct);
        return new Scope(this, request);
    }
    public ValueTask DisposeAsync()
    {
        if (!disposed) { disposed = true; revoked.Cancel(); }
        return ValueTask.CompletedTask;
    }
    private sealed class Scope(SyntheticZipParentRetentionGrant root, ZipInitialRetentionRequest request)
        : IZipAuthorizedEffectScope<ZipInitialRetentionRequest>
    {
        private bool disposed;
        public object Root => root;
        public ZipInitialRetentionRequest Request => request;
        public string ExecutingActorId => root.ExecutingActorId;
        public DateTimeOffset ValidUntilUtc => root.ValidUntilUtc;
        public CancellationToken Invalidated => root.Revoked;
        public ValueTask RevalidateAsync(CancellationToken ct = default)
        {
            if (disposed) throw new UnauthorizedAccessException("Synthetic retention scope disposed.");
            root.Check(ct); return ValueTask.CompletedTask;
        }
        public IDisposable Accept(CancellationToken ct = default)
        {
            if (disposed) throw new UnauthorizedAccessException("Synthetic retention scope disposed.");
            root.Check(ct); return new Acceptance();
        }
        private sealed class Acceptance : IDisposable { public void Dispose() { } }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            if (root.BeforeDisposal is not null) await root.BeforeDisposal();
            disposed = true; // Normal scope closure does not report authority invalidation.
        }
    }
}
