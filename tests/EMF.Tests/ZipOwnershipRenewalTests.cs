using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipOwnershipRenewalTests
{
    [Fact]
    public async Task Renewal_changes_only_expiry_and_does_not_invent_authority()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var before = await f.CreateParent(); clock.Now += TimeSpan.FromMinutes(29);
        var until = await f.Journal.RenewOwnershipAsync("zip", "worker", before.Fence.Epoch, TimeSpan.FromMinutes(15));
        var after = await f.Read();
        Assert.Equal(clock.Now.AddMinutes(15), until);
        Assert.Equal(before.Fence, after.Fence); Assert.Equal(before.Binding, after.Binding);
        Assert.Equal(before.State, after.State); Assert.Equal(before.Budget, after.Budget);
        Assert.Equal(before.Plan, after.Plan); Assert.Equal(before.Entries, after.Entries);
        Assert.Equal(until, after.OwnerUntil); Assert.Null(await f.Journal.ReadAdmissionAsync("zip"));
        Assert.Equal(until, await f.Journal.RenewOwnershipAsync("zip", "worker", before.Fence.Epoch, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Wrong_owner_epoch_expired_lease_and_stale_takeover_cannot_renew()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var before = await f.CreateParent();
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync("zip", "other", before.Fence.Epoch, TimeSpan.FromMinutes(15)));
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync("zip", "worker", before.Fence.Epoch + 1, TimeSpan.FromMinutes(15)));
        Assert.Equal(before.Fence, (await f.Read()).Fence); Assert.Equal(before.OwnerUntil, (await f.Read()).OwnerUntil);
        clock.Now = before.OwnerUntil;
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync("zip", "worker", before.Fence.Epoch, TimeSpan.FromMinutes(15)));
        var takeover = await f.Journal.ClaimAsync("zip", "other", TimeSpan.FromMinutes(15));
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync("zip", "worker", before.Fence.Epoch, TimeSpan.FromMinutes(15)));
        Assert.Equal(takeover.Fence, (await f.Read()).Fence);
    }

    private sealed class TimerClock : TimeProvider
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TimerCallback? _callback; private object? _state;
        public void Fire() => _callback!(_state);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; Ready.TrySetResult(); return new Timer(); }
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class RenewalJournal(int failAt, bool released = false) : IZipOwnershipJournal
    {
        public int Calls;
        public Task<DateTimeOffset> RenewOwnershipAsync(string operationId, string owner, long epoch, TimeSpan duration, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (++Calls == failAt)
            { if (released) throw new ZipOwnershipReleasedException(); throw new ZipFenceException(); }
            return Task.FromResult(DateTimeOffset.UtcNow + duration);
        }
    }

    [Fact]
    public async Task Failed_heartbeat_cancels_worker_and_surfaces_ownership_failure()
    {
        var clock = new TimerClock(); var journal = new RenewalJournal(2); CancellationToken worker = default;
        var run = new ZipOwnershipHeartbeat(journal, clock).RunAsync(new("zip", "worker", 1, 1, null, 0), async ct =>
        { worker = ct; await Task.Delay(Timeout.InfiniteTimeSpan, ct); return true; });
        await clock.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); clock.Fire();
        await Assert.ThrowsAsync<ZipFenceException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(worker.IsCancellationRequested); Assert.Equal(2, journal.Calls);
    }

    [Fact]
    public async Task Failed_initial_renewal_never_starts_worker()
    {
        var called = false;
        await Assert.ThrowsAsync<ZipFenceException>(() => new ZipOwnershipHeartbeat(new RenewalJournal(1)).RunAsync(
            new("zip", "worker", 1, 1, null, 0), ct => { called = true; return Task.FromResult(true); }));
        Assert.False(called);
    }

    [Fact]
    public async Task Release_racing_renewal_stops_heartbeat_without_reporting_ownership_loss()
    {
        var clock = new TimerClock(); var journal = new RenewalJournal(2, released: true);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new ZipOwnershipHeartbeat(journal, clock).RunAsync(new("zip", "worker", 1, 1, null, 0), async ct =>
        { await finish.Task; ct.ThrowIfCancellationRequested(); return true; });
        await clock.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); clock.Fire(); finish.SetResult();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(10))); Assert.Equal(2, journal.Calls);
    }

    [Fact]
    public async Task Released_parent_with_same_live_owner_ends_renewal_without_mutation()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        // This integration path opens the real-time ingestion adapter as well.
        var f = new ZipDurableRuntimeFixture(evidence);
        var owned = await f.CreateParent(); var released = await f.Driver.RunAsync(owned);
        Assert.Equal(ZipParentState.Released, released.State);
        await Assert.ThrowsAsync<ZipOwnershipReleasedException>(() => f.Journal.RenewOwnershipAsync("zip", "worker", released.Fence.Epoch, TimeSpan.FromMinutes(15)));
        Assert.Equal(released.Fence, (await f.Read()).Fence);
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync("zip", "other", released.Fence.Epoch, TimeSpan.FromMinutes(15)));
    }
}
