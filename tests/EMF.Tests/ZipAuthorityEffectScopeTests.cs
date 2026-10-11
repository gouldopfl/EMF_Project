using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

// Tests synthetic lifecycle mechanics, not production issuer authenticity.
public sealed class ZipAuthorityEffectScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absolute_expiry_bounds_initial_revalidation_and_scope_acceptance_waits(bool scopeWait)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var clock = new Clock(f.Clock.Now);
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, clock);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Wait() { waiting.TrySetResult(); return release.Task; }
        if (scopeWait) grant.BeforeScopeAcceptance = Wait; else grant.BeforeRevalidation = Wait;
        var request = await f.RequestAsync(grant.Purpose);
        var claim = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, f.Profile, clock).ClaimAsync(grant, request);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromMinutes(45));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim.WaitAsync(TimeSpan.FromSeconds(10)));
        release.SetResult();
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private readonly List<Timer> timers = [];
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            foreach (var timer in timers.ToArray()) timer.Fire(now);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, now + dueTime); timers.Add(timer); return timer;
        }
        private sealed class Timer(TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            private bool disposed;
            public void Fire(DateTimeOffset current) { if (!disposed && current >= due) { disposed = true; callback(state); } }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class WaitingJournal(IZipOwnershipJournal inner) : IZipOwnershipJournal
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Commits;
        public async Task<ZipParentSnapshot> ClaimOwnershipAsync(ZipOwnershipClaimRequest request, CancellationToken ct = default)
        {
            Waiting.TrySetResult(); await Release.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            var result = await inner.ClaimOwnershipAsync(request, ct); Commits++; return result;
        }
        public Task<DateTimeOffset> RenewOwnershipAsync(string operationId, string owner, long epoch, TimeSpan duration, CancellationToken ct = default)
            => inner.RenewOwnershipAsync(operationId, owner, epoch, duration, ct);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("revocation")]
    [InlineData("disposal")]
    public async Task Invalidation_during_awaited_journal_call_cancels_before_commit(string invalidate)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var clock = new Clock(f.Clock.Now);
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, clock);
        var journal = new WaitingJournal(f.Journal);
        var request = await f.RequestAsync(grant.Purpose);
        var claim = new ZipAuthorizedOwnershipService(f.Journal, journal, f.Profile, clock).ClaimAsync(grant, request);
        await journal.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (invalidate == "expiry") clock.Advance(TimeSpan.FromMinutes(45));
        else if (invalidate == "revocation") grant.Revoke(); else await grant.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim.WaitAsync(TimeSpan.FromSeconds(10)));
        journal.Release.SetResult();
        Assert.Equal(0, journal.Commits);
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_scope_or_root_cannot_admit_callback(bool rootDisposed)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var scope = await grant.AuthorizeOwnershipEffectAsync(await f.RequestAsync(grant.Purpose));
        if (rootDisposed) await grant.DisposeAsync(); else await scope.DisposeAsync();
        var called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ExecuteAsync(_ =>
        { called = true; return f.Journal.ReadAsync(f.Admission.OperationId)!; }));
        Assert.False(called);
    }

    [Fact]
    public async Task Scope_is_single_use_and_ownership_renewal_does_not_extend_root_deadline()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        var request = await f.RequestAsync(grant.Purpose);
        await using var scope = await grant.AuthorizeOwnershipEffectAsync(request);
        var owned = await scope.ExecuteAsync(ct => f.Journal.ClaimOwnershipAsync(request, ct));
        var calls = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ExecuteAsync(_ => { calls++; return Task.FromResult(owned); }));
        Assert.Equal(0, calls);
        var deadline = grant.ValidUntilUtc;
        f.Clock.Now += TimeSpan.FromMinutes(14);
        await f.Journal.RenewOwnershipAsync(f.Admission.OperationId, owned.Fence.Owner, owned.Fence.Epoch, f.Profile.OwnershipLease);
        Assert.Equal(deadline, grant.ValidUntilUtc); Assert.Equal(deadline, scope.ValidUntilUtc);
        f.Clock.Now = deadline;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grant.RevalidateAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => grant.OpenChildAsync(owned, null!));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("deadline")]
    [InlineData("root")]
    public async Task Scope_cannot_widen_actor_deadline_or_attempt(string change)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var other = new SyntheticZipExecutionGrant(f.Admission, grant.Purpose, f.Clock);
        grant.ChangeScope = scope => change switch
        {
            "actor" => new SyntheticZipExecutionGrant.Scope(grant, scope.Request, f.Clock) { ExecutingActorId = "other" },
            "deadline" => new SyntheticZipExecutionGrant.Scope(grant, scope.Request, f.Clock) { ValidUntilUtc = grant.ValidUntilUtc.AddSeconds(1) },
            _ => new SyntheticZipExecutionGrant.Scope(grant, scope.Request, f.Clock) { Root = other }
        };
        var request = await f.RequestAsync(grant.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ZipAuthorizedOwnershipService(f.Journal, f.Journal, f.Profile, f.Clock).ClaimAsync(grant, request));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }
}
