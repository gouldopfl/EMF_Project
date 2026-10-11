using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;
namespace EMF.Orchestration.Services;

internal sealed class ZipParentRetentionAttempt : IAsyncDisposable
{
    private readonly IZipAuthorizedParentRetention root;
    private readonly ZipParentAdmission original;
    private readonly string actor;
    private readonly CancellationTokenSource deadline,lifetime;
    internal TimeProvider Clock { get; }
    internal ZipParentAdmission Current { get; set; }
    internal ZipRetainedBinding? Source { get; set; }
    internal ZipParentRetentionRecord? Retention { get; set; }
    internal DateTimeOffset ValidUntilUtc { get; }
    internal CancellationToken Invalidated => lifetime.Token;
    private bool disposed;
    internal ZipParentRetentionAttempt(IZipAuthorizedParentRetention root,TimeProvider clock,CancellationToken ct)
    {
        this.root=root;original=root.Admission;Current=original;actor=root.ExecutingActorId;
        Clock=clock;ValidUntilUtc=root.ValidUntilUtc;
        if(original.State is not (ZipAdmissionState.Approved or ZipAdmissionState.SourceBound) || string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("Initial retention requires an authenticated approved source admission.");
        var remaining=ValidUntilUtc-clock.GetUtcNow();
        if(remaining<=TimeSpan.Zero)throw new UnauthorizedAccessException("Retention authority expired.");
        deadline=new(remaining,clock);lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,root.Revoked,deadline.Token);
    }
    private void Check()
    {
        if(disposed || root.Admission!=original || root.ExecutingActorId!=actor || root.ValidUntilUtc!=ValidUntilUtc ||
            Clock.GetUtcNow()>=ValidUntilUtc || Current.Key!=original.Key || Current.Binding!=original.Binding || Current.OperationId!=original.OperationId)
            throw new UnauthorizedAccessException("Retention authority anchor changed or expired.");
        Invalidated.ThrowIfCancellationRequested();root.Revoked.ThrowIfCancellationRequested();
    }
    internal async Task RevalidateAsync(CancellationToken ct)
    {Check();using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,Invalidated);await root.RevalidateAsync(linked.Token);linked.Token.ThrowIfCancellationRequested();Check();}
    internal ZipPhaseIIBoundary<ZipInitialRetentionRequest> Boundary()
        => new(root,actor,ValidUntilUtc,Clock,Invalidated,RevalidateAsync,Check,Invalidate,
            () => new(Current,Source,Retention,original.Binding.ParentNamespaceId),(r,ct) => root.AuthorizeRetentionEffectAsync(r,ct));
    private void Invalidate(){try{if(!disposed)lifetime.Cancel();}catch(ObjectDisposedException){/*Already ended.*/}}
    public ValueTask DisposeAsync()
    {if(!disposed){disposed=true;lifetime.Cancel();lifetime.Dispose();deadline.Dispose();}return ValueTask.CompletedTask;}
}
