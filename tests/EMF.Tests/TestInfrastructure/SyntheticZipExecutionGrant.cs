using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Tests.TestInfrastructure;

// Synthetic contract mechanics only; no authenticated issuer or external
// revocation/commit ordering guarantee is simulated here.
internal sealed class SyntheticZipExecutionGrant(ZipParentAdmission admission,
    ZipOwnershipPurpose purpose, TimeProvider clock) : IZipAuthorizedExecution
{
    private readonly CancellationTokenSource lifetime = new();
    public ZipParentAdmission Admission { get; } = admission;
    public ZipOwnershipPurpose Purpose { get; } = purpose;
    public string ExecutingActorId => "synthetic-current-actor";
    public DateTimeOffset ValidUntilUtc { get; } = clock.GetUtcNow().AddMinutes(45);
    public CancellationToken Revoked => lifetime.Token;
    public int Checks;
    public bool Denied;
    public Action? OnCheck;
    public Func<Task>? BeforeRevalidation;
    public Func<Task>? BeforeScopeAcceptance;
    public Func<Task>? BeforeScopeDisposal;
    public Func<Scope, IZipAuthorizedOwnershipScope>? ChangeScope;
    public void Revoke() => lifetime.Cancel();
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
