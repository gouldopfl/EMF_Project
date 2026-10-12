using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Tests.TestInfrastructure;

// Synthetic contract mechanics only; no authenticated issuer or external
// revocation/commit ordering guarantee is simulated here.
internal sealed partial class SyntheticZipExecutionGrant(ZipParentAdmission admission,
    ZipOwnershipPurpose purpose, TimeProvider clock, TimeSpan? grantLifetime = null) : IZipAuthorizedExecution
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly object authorityGate = new();
    public ZipParentAdmission Admission { get; } = admission;
    public ZipOwnershipPurpose Purpose { get; } = purpose;
    public string ExecutingActorId => "synthetic-current-actor";
    public DateTimeOffset ValidUntilUtc { get; } = clock.GetUtcNow().Add(grantLifetime ?? TimeSpan.FromMinutes(45));
    public CancellationToken Revoked => lifetime.Token;
    public int Checks;
    public bool Denied;
    public Action? OnCheck;
    public Func<Task>? BeforeRevalidation;
    public Func<Task>? BeforeScopeAcceptance;
    public Func<Task>? BeforeScopeDisposal;
    public Func<Scope, IZipAuthorizedOwnershipScope>? ChangeScope;
    public Action<object>? OnEffectRequest;
    internal Action<object>? AfterEffectAcceptance {get;set;}
    public Func<Task>? BeforeEffectDisposal;
    public Action? RevokeCurrentEffect;
    public TimeSpan? EffectLifetime;
    public int EffectRequests, EffectDisposals;
    public void Revoke() { lock(authorityGate) lifetime.Cancel(); }
    public async Task RevalidateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Revoked.ThrowIfCancellationRequested();
        if (BeforeRevalidation is not null) await BeforeRevalidation().WaitAsync(ct);
        Checks++; OnCheck?.Invoke();
        Revoked.ThrowIfCancellationRequested();
        if (Denied || clock.GetUtcNow() >= ValidUntilUtc) throw new UnauthorizedAccessException();
    }
    public async Task<IZipAuthorizedOwnershipScope> AuthorizeOwnershipEffectAsync(ZipOwnershipClaimRequest request, CancellationToken ct = default)
    {
        await RevalidateAsync(ct);
        if (request.Purpose != Purpose || request.ExpectedAdmission != Admission) throw new UnauthorizedAccessException();
        var scope = new Scope(this, request, clock);
        return ChangeScope?.Invoke(scope) ?? scope;
    }
    public Task<IZipAuthorizedEffectScope<ZipParentReadRequest>> AuthorizeParentReadAsync(ZipParentReadRequest request, CancellationToken ct = default)
        => IssueEffectAsync(request,request.Admission,ct);
    public Task<IZipAuthorizedEffectScope<ZipPlanAdmissionRequest>> AuthorizePlanAdmissionAsync(ZipPlanAdmissionRequest request, CancellationToken ct = default)
        => IssueEffectAsync(request,request.Admission,ct);
    public Task<IZipAuthorizedEffectScope<ZipChildExtractionRequest>> AuthorizeChildExtractionAsync(ZipChildExtractionRequest request, CancellationToken ct = default)
        => IssueEffectAsync(request,request.Admission,ct);
    public Task<IZipAuthorizedEffectScope<ZipChildRetentionRequest>> AuthorizeChildRetentionAsync(ZipChildRetentionRequest request, CancellationToken ct = default)
        => IssueEffectAsync(request,request.Admission,ct);
    private async Task<IZipAuthorizedEffectScope<T>> IssueEffectAsync<T>(T request,ZipParentAdmission expected,CancellationToken ct) where T : class
    {
        await RevalidateAsync(ct);
        if(expected!=Admission)throw new UnauthorizedAccessException("Synthetic effect admission differs.");
        var scope=new EffectScope<T>(this,request,clock);
        RevokeCurrentEffect=scope.Revoke;
        EffectRequests++;
        try { OnEffectRequest?.Invoke(request);return scope; }
        catch { await scope.DisposeAsync();throw; }
    }
    private void CheckEffect()
    {
        Revoked.ThrowIfCancellationRequested();
        if(Denied || clock.GetUtcNow()>=ValidUntilUtc)throw new UnauthorizedAccessException("Synthetic effect root denied or expired.");
    }
    internal sealed class EffectScope<T> : IZipAuthorizedEffectScope<T> where T : class
    {
        private readonly SyntheticZipExecutionGrant root;
        private readonly TimeProvider clock;
        private readonly CancellationTokenSource deadline,lifetime;
        private bool disposed;
        public object Root { get; }
        public T Request { get; }
        public string ExecutingActorId { get; }
        public DateTimeOffset ValidUntilUtc { get; }
        public CancellationToken Invalidated { get; }
        internal EffectScope(SyntheticZipExecutionGrant root,T request,TimeProvider clock)
        {
            this.root=root;this.clock=clock;Root=root;Request=request;
            ExecutingActorId=root.ExecutingActorId;
            var requestedDeadline=root.EffectLifetime is { } duration ? clock.GetUtcNow()+duration : root.ValidUntilUtc;
            ValidUntilUtc=requestedDeadline<root.ValidUntilUtc ? requestedDeadline : root.ValidUntilUtc;
            var remaining=ValidUntilUtc-clock.GetUtcNow();
            if(remaining<=TimeSpan.Zero)throw new UnauthorizedAccessException("Synthetic effect expired.");
            deadline=new(remaining,clock);
            lifetime=CancellationTokenSource.CreateLinkedTokenSource(root.Revoked,deadline.Token);
            Invalidated=lifetime.Token;
        }
        public void Revoke() { lock(root.authorityGate) lifetime.Cancel(); }
        private void Check(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Invalidated.ThrowIfCancellationRequested();root.CheckEffect();
            if(disposed || clock.GetUtcNow()>=ValidUntilUtc)throw new UnauthorizedAccessException("Synthetic effect disposed or expired.");
        }
        public async ValueTask RevalidateAsync(CancellationToken ct=default)
        {
            Check(ct);await root.RevalidateAsync(ct);
            lock(root.authorityGate)Check(ct);
        }
        public IDisposable Accept(CancellationToken ct=default)
        {
            Monitor.Enter(root.authorityGate);
            try { Check(ct);root.AfterEffectAcceptance?.Invoke(Request);return new Accepted(root.authorityGate); }
            catch { Monitor.Exit(root.authorityGate);throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if(disposed)return;
            try { if(root.BeforeEffectDisposal is not null)await root.BeforeEffectDisposal(); }
            finally
            {
                disposed=true;root.EffectDisposals++;
                // Disposal releases resources; only explicit invalidation/expiry cancels authority.
                lifetime.Dispose();deadline.Dispose();
            }
        }
        private sealed class Accepted(object gate) : IDisposable
        {
            private bool disposed;
            public void Dispose(){if(!disposed){disposed=true;Monitor.Exit(gate);}}
        }
    }
    public Task<ZipChildIngestionRuntime> OpenChildAsync(ZipParentSnapshot p, ZipEntryProgress e, CancellationToken ct = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() { lifetime.Cancel(); return ValueTask.CompletedTask; }

    internal sealed class Scope(SyntheticZipExecutionGrant root, ZipOwnershipClaimRequest request, TimeProvider clock) : IZipAuthorizedOwnershipScope
    {
        private readonly CancellationTokenSource lifetime = new();
        private int used;
        public IZipAuthorizedExecution Root { get; init; } = root;
        public ZipOwnershipClaimRequest Request { get; init; } = request;
        public string ExecutingActorId { get; init; } = root.ExecutingActorId;
        public DateTimeOffset ValidUntilUtc { get; init; } = root.ValidUntilUtc;
        public CancellationToken Revoked => lifetime.Token;
        public async Task<ZipParentSnapshot> ExecuteAsync(Func<CancellationToken, Task<ZipParentSnapshot>> effect, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref used, 1) != 0) throw new UnauthorizedAccessException("Scope already consumed.");
            var remaining = ValidUntilUtc - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) throw new UnauthorizedAccessException();
            using var deadline = new CancellationTokenSource(remaining, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, root.Revoked, Revoked, deadline.Token);
            ct = linked.Token; ct.ThrowIfCancellationRequested();
            if (root.BeforeScopeAcceptance is not null) await root.BeforeScopeAcceptance().WaitAsync(ct);
            await root.RevalidateAsync(ct);
            ct.ThrowIfCancellationRequested();
            var result = await effect(ct);
            // The service must independently revalidate before handoff. This
            // fake does not mask a missing service check with its own postcheck.
            ct.ThrowIfCancellationRequested();
            return result;
        }
        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            if (root.BeforeScopeDisposal is not null) await root.BeforeScopeDisposal();
        }
    }
}
