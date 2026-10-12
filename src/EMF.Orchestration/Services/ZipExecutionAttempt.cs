using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

// Constructed only from a successful same-root authorized ownership handoff.
// The host retains ownership of the root and must dispose it after this attempt.
public sealed partial class ZipExecutionAttempt : IAsyncDisposable
{
    private sealed class Consumption { internal int Used; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IZipAuthorizedExecution,Consumption> consumed = new();
    private readonly IZipAuthorizedExecution root;
    private readonly CancellationTokenSource deadline, lifetime;
    private readonly ZipParentAdmission admission;
    private readonly ZipOwnershipPurpose purpose;
    private readonly string actor;
    private bool disposed;
    internal TimeProvider Clock { get; }
    public ZipParentSnapshot Claimed { get; }
    public DateTimeOffset ValidUntilUtc { get; }
    public CancellationToken Invalidated => lifetime.Token;
    public ZipParentAdmission Admission => admission;
    private ZipExecutionAttempt(IZipAuthorizedExecution root, ZipParentSnapshot claimed, TimeProvider clock, CancellationToken ct)
    {
        this.root = root; Claimed = claimed; Clock = clock; admission = root.Admission;
        purpose = root.Purpose; actor = root.ExecutingActorId; ValidUntilUtc = root.ValidUntilUtc;
        var remaining = ValidUntilUtc - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new UnauthorizedAccessException("ZIP attempt expired.");
        deadline = new(remaining, clock);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, root.Revoked, deadline.Token);
        try { Validate(claimed); } catch { lifetime.Dispose(); deadline.Dispose(); throw; }
    }
    public static async Task<ZipExecutionAttempt> ClaimAsync(ZipAuthorizedOwnershipService ownership,
        IZipAuthorizedExecution grant, ZipOwnershipClaimRequest request, TimeProvider? timeProvider = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if(Interlocked.Exchange(ref consumed.GetOrCreateValue(grant).Used,1)!=0)
            throw new UnauthorizedAccessException("Each execution attempt requires a fresh root grant.");
        var claimed = await ownership.ClaimAsync(grant, request, ct);
        var attempt = new ZipExecutionAttempt(grant, claimed, timeProvider ?? TimeProvider.System, ct);
        try { await attempt.RevalidateAsync(ct); return attempt; }
        catch { await attempt.DisposeAsync(); throw; }
    }
    internal void Validate(ZipParentSnapshot parent)
    {
        Check();
        if (parent.OwnerUntil <= Clock.GetUtcNow()) throw new ZipFenceException();
        if (parent.Binding != Claimed.Binding || parent.Fence.Owner != Claimed.Fence.Owner ||
            parent.Fence.Epoch != Claimed.Fence.Epoch ||
            admission.Parent != parent.Binding || admission.State != ZipAdmissionState.ParentBound)
            throw new UnauthorizedAccessException("ZIP attempt ownership or parent anchor changed.");
    }
    internal void Check()
    {
        if (disposed || root.Admission != admission || root.Purpose != purpose || root.ExecutingActorId != actor ||
            root.ValidUntilUtc != ValidUntilUtc || Clock.GetUtcNow() >= ValidUntilUtc)
            throw new UnauthorizedAccessException("ZIP attempt anchor changed or expired.");
        Invalidated.ThrowIfCancellationRequested(); root.Revoked.ThrowIfCancellationRequested();
    }
    public async Task RevalidateAsync(CancellationToken ct = default)
    {
        Check(); using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Invalidated);
        await root.RevalidateAsync(linked.Token); linked.Token.ThrowIfCancellationRequested(); Check();
    }
    internal ZipPhaseIIBoundary<T> Boundary<T>(Func<T> request, Func<IZipAuthorizedExecution,T,CancellationToken,
        Task<IZipAuthorizedEffectScope<T>>> issue) where T : class
        => new(root, actor, ValidUntilUtc, Clock, Invalidated, RevalidateAsync, Check, Invalidate, request,
            (r,ct) => issue(root,r,ct));
    internal ZipPhaseIIBoundary<ZipParentReadRequest> ParentReadBoundary(Func<ZipParentSnapshot> current)
        => Boundary(() => { var p = current(); Validate(p); return new ZipParentReadRequest(admission,p); },
            (g,r,ct) => g.AuthorizeParentReadAsync(r,ct));
    internal ZipPhaseIIBoundary<ZipPlanAdmissionRequest> PlanBoundary(Func<ZipParentSnapshot> current)
        => Boundary(() => { var p = current(); Validate(p); return new ZipPlanAdmissionRequest(admission,p); },
            (g,r,ct) => g.AuthorizePlanAdmissionAsync(r,ct));
    internal ZipPhaseIIBoundary<ZipChildExtractionRequest> ExtractionBoundary(Func<ZipParentSnapshot> current, int ordinal)
        => Boundary(() => { var p = current(); Validate(p); return new ZipChildExtractionRequest(admission,p,
            p.Entries.Single(e => e.Plan.FileOrdinal == ordinal).Plan); }, (g,r,ct) => g.AuthorizeChildExtractionAsync(r,ct));
    internal ZipPhaseIIBoundary<ZipChildRetentionRequest> RetentionBoundary(Func<ZipParentSnapshot> current,
        int ordinal, Func<ZipRetentionRecord?> retained, string namespaceId)
        => Boundary(() => { var p = current(); Validate(p); return new ZipChildRetentionRequest(admission,p,
            p.Entries.Single(e => e.Plan.FileOrdinal == ordinal).Plan,retained(),namespaceId); },
            (g,r,ct) => g.AuthorizeChildRetentionAsync(r,ct));
    internal void Invalidate() { try { if(!disposed)lifetime.Cancel(); } catch(ObjectDisposedException) { /* Already ended. */ } }
    public ValueTask DisposeAsync()
    {
        if (!disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); deadline.Dispose(); }
        return ValueTask.CompletedTask;
    }
}

// Refresh exact scoped requests after every wait, including after journal
// transitions advance a projection. Authority is never reduced to a Boolean.
internal sealed class ZipPhaseIIBoundary<T>(object root, string actor, DateTimeOffset rootDeadline,
    TimeProvider clock, CancellationToken invalidated, Func<CancellationToken,Task> validateRoot,
    Action checkRoot, Action invalidateAttempt, Func<T> request, Func<T,CancellationToken,Task<IZipAuthorizedEffectScope<T>>> issue)
    : IZipEffectBoundaryGuard, IAsyncDisposable where T : class
{
    private IZipAuthorizedEffectScope<T>? scope;
    private T? issuedRequest;
    private CancellationTokenSource? scopeDeadline;
    private CancellationTokenRegistration registration,deadlineRegistration;
    private readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(invalidated);
    private bool disposed;
    private DateTimeOffset lastDeadline;
    public CancellationToken Invalidated => lifetime.Token;
    public async ValueTask RevalidateAsync(CancellationToken ct = default)
    {

        try
        {
        if (disposed) throw new UnauthorizedAccessException("ZIP boundary disposed.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Invalidated);
        ct = linked.Token; ct.ThrowIfCancellationRequested();
        await ReleaseScopeAsync();
        await validateRoot(ct); ct.ThrowIfCancellationRequested(); checkRoot();
        var expected = request();
        issuedRequest=expected;
        scope = await issue(expected,ct);
        if (!ReferenceEquals(scope.Root,root) || !EqualityComparer<T>.Default.Equals(scope.Request,expected) ||
            scope.ExecutingActorId != actor || scope.ValidUntilUtc > rootDeadline)
            throw new UnauthorizedAccessException("ZIP effect scope anchors differ.");
        lastDeadline = scope.ValidUntilUtc;
        var remaining = lastDeadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new UnauthorizedAccessException("ZIP effect scope expired.");
        scopeDeadline = new(remaining,clock);
        registration = scope.Invalidated.Register(() => { lifetime.Cancel(); invalidateAttempt(); });
        deadlineRegistration=scopeDeadline.Token.Register(() => { lifetime.Cancel(); invalidateAttempt(); });
        await scope.RevalidateAsync(ct); await validateRoot(ct);
        ct.ThrowIfCancellationRequested(); Check();
        }
        catch { invalidateAttempt(); throw; }
    }
    private void Check()
    {
        checkRoot(); Invalidated.ThrowIfCancellationRequested();
        if (disposed || scope is null || !ReferenceEquals(scope.Root,root) || scope.ExecutingActorId!=actor ||
            clock.GetUtcNow() >= lastDeadline || scope.ValidUntilUtc != lastDeadline ||
            !EqualityComparer<T>.Default.Equals(scope.Request,request()))
        { invalidateAttempt();throw new UnauthorizedAccessException("ZIP effect scope is stale or expired."); }
        scope.Invalidated.ThrowIfCancellationRequested();
    }
    public IDisposable Accept(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested(); Check();
            var acceptance = scope!.Accept(ct) ?? throw new UnauthorizedAccessException("ZIP scope returned no acceptance capability.");
            try { ct.ThrowIfCancellationRequested(); Check(); return acceptance; }
            catch { acceptance.Dispose(); throw; }
        }
        catch { invalidateAttempt(); throw; }
    }
    private async ValueTask ReleaseScopeAsync()
    {
        var previous=scope;
        if(previous is null)return;
        var signal=previous.Invalidated;
        var expires=lastDeadline;
        try
        {
            await previous.DisposeAsync();
            signal.ThrowIfCancellationRequested(); Invalidated.ThrowIfCancellationRequested();
            if(!ReferenceEquals(previous.Root,root)||previous.ExecutingActorId!=actor||previous.ValidUntilUtc!=expires||
                !EqualityComparer<T>.Default.Equals(previous.Request,issuedRequest))
            {invalidateAttempt();throw new UnauthorizedAccessException("ZIP scope anchor changed during disposal.");}
            if(clock.GetUtcNow()>=expires){invalidateAttempt();throw new UnauthorizedAccessException("ZIP scope expired during disposal.");}
            checkRoot();
        }
        finally
        {
            registration.Dispose();deadlineRegistration.Dispose();scopeDeadline?.Dispose();scopeDeadline=null;scope=null;issuedRequest=null;
        }
    }
    internal async Task FinishAsync(CancellationToken ct)
    {
        Invalidated.ThrowIfCancellationRequested();
        await ReleaseScopeAsync();
        await validateRoot(ct); ct.ThrowIfCancellationRequested(); checkRoot();
        if (lastDeadline != default && clock.GetUtcNow() >= lastDeadline) throw new UnauthorizedAccessException("ZIP effect expired during disposal.");
        disposed = true;
    }
    public async ValueTask DisposeAsync()
    {
        try { if (!disposed) { disposed = true; await ReleaseScopeAsync(); } }
        finally { lifetime.Dispose(); }
    }
}
