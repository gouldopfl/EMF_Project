using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Orchestration;
using Xunit;

namespace EMF.Tests;

public sealed class ReviewerAssemblyRetainedReaderLifetimeTests
{
    [Fact]
    public async Task Detached_mutable_graphs_and_original_buffer_registry_preserve_isolation()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();var first=c.Acquire();using var sibling=c.Acquire();
        var one=await ((IEvidenceRepository)first).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        var nested=Assert.IsType<Dictionary<string,object>>(one!.Metadata["z"]);var owned=Assert.IsType<byte[]>(nested["payload"]);
        byte[] foreign={99,98};nested.Remove("payload");nested["payload"]=foreign;
        var again=await ((IEvidenceRepository)first).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        var other=await ((IEvidenceRepository)sibling).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        var againBytes=Assert.IsType<byte[]>(Assert.IsType<Dictionary<string,object>>(again!.Metadata["z"])["payload"]);
        var otherBytes=Assert.IsType<byte[]>(Assert.IsType<Dictionary<string,object>>(other!.Metadata["z"])["payload"]);
        Assert.NotSame(owned,againBytes);Assert.NotSame(againBytes,otherBytes);Assert.Equal(new byte[]{7,8,9},againBytes);
        first.Dispose();Assert.All(owned,b=>Assert.Equal((byte)0,b));Assert.All(againBytes,b=>Assert.Equal((byte)0,b));
        Assert.Equal(new byte[]{99,98},foreign);Assert.Equal(new byte[]{7,8,9},otherBytes);
    }
    [Fact]
    public async Task Context_close_preserves_live_children_and_sibling_disposal_is_local()
    {
        var c=ReviewerAssemblyRetainedReaderFixture.Context();var first=c.Acquire();var second=c.Acquire();
        var a=await ((IArtifactContentStore)first).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        var b=await ((IArtifactContentStore)second).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        c.Dispose();Assert.Throws<ObjectDisposedException>(()=>c.Acquire());
        Assert.Equal("synthetic\n",await ((IArtifactTextExtractor)second).ExtractTextAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        long spent=c.Accounting.Spent;first.Dispose();Assert.All(a!,x=>Assert.Equal((byte)0,x));Assert.Equal("synthetic\n",System.Text.Encoding.UTF8.GetString(b!));
        Assert.Equal(spent,c.Accounting.Spent);Assert.True(c.Accounting.Live>0);
        Task drain=c.DisposeAsync().AsTask();Assert.False(drain.IsCompleted);second.Dispose();await ReviewerAssemblyRetainedReaderFixture.Bounded(drain);
        Assert.Equal(0,c.Accounting.Live);Assert.Equal(spent,c.Accounting.Spent);c.Dispose();second.Dispose();await c.DisposeAsync();
    }
    [Fact]
    public async Task Caller_stream_is_owned_by_caller_but_borrows_session_bytes()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();var s=c.Acquire();var bytes=await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        using var wrapper=new MemoryStream(bytes!,false);Assert.Equal((int)'s',wrapper.ReadByte());s.Dispose();wrapper.Position=0;
        Assert.Equal(0,wrapper.ReadByte()); // wrapper remains open; its borrowed allocation was cleared
    }
    [Fact]
    public async Task One_active_one_waiter_third_rejection_and_waiter_cancellation_are_bounded()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();using var s=c.Acquire();using var token=new CancellationTokenSource();
        var entered=ReviewerAssemblyRetainedReaderFixture.Barrier();var release=ReviewerAssemblyRetainedReaderFixture.Barrier();
        using var unblock=ReviewerAssemblyRetainedReaderFixture.ReleaseOnDispose(release);
        s.ProjectionCheckpoint=async phase=>{if(phase=="BeforePrecount"){entered.TrySetResult();await release.Task.ConfigureAwait(false);}};
        long baseline=s.Accounting.Live;var first=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);await ReviewerAssemblyRetainedReaderFixture.Bounded(entered.Task);
        var queued=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact,token.Token);
        Assert.Equal(baseline+256,s.Accounting.Live);Assert.Equal(2*ReviewerAssemblyRetainedReaderFixture.Caps.MaximumWorkPerQuery,s.Accounting.Reserved);
        var overlap=await Assert.ThrowsAsync<InvalidOperationException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal("SessionOverlap",overlap.Message);
        long spent=s.Accounting.Spent;token.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>queued.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(baseline,s.Accounting.Live);Assert.Equal(spent,s.Accounting.Spent);Assert.Equal(ReviewerAssemblyRetainedReaderFixture.Caps.MaximumWorkPerQuery,s.Accounting.Reserved);Assert.Equal(0,s.Accounting.Queued);
        release.TrySetResult();await ReviewerAssemblyRetainedReaderFixture.Bounded(first);Assert.Equal(0,s.Accounting.Reserved);
    }
    [Fact]
    public async Task Sole_waiter_promotes_without_copy_aliases_or_work_allowance_reset()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();using var s=c.Acquire();
        var entered=ReviewerAssemblyRetainedReaderFixture.Barrier();var release=ReviewerAssemblyRetainedReaderFixture.Barrier();int visits=0;
        using var unblock=ReviewerAssemblyRetainedReaderFixture.ReleaseOnDispose(release);
        s.ProjectionCheckpoint=async phase=>{if(phase=="BeforePrecount"&&Interlocked.Increment(ref visits)==1){entered.TrySetResult();await release.Task.ConfigureAwait(false);}};
        var first=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);await ReviewerAssemblyRetainedReaderFixture.Bounded(entered.Task);
        var second=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);long spent=s.Accounting.Spent;
        release.TrySetResult();await ReviewerAssemblyRetainedReaderFixture.Bounded(Task.WhenAll(first,second));
        Assert.NotSame(first.Result,second.Result);Assert.Equal(first.Result,second.Result);Assert.True(s.Accounting.Spent>spent);Assert.Equal(0,s.Accounting.Reserved);Assert.Equal(0,s.Accounting.Queued);Assert.Equal(2,s.Accounting.Buffers);
    }
    [Fact]
    public async Task Closing_drains_active_projection_rejects_waiter_and_refunds_only_after_cleanup()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();var s=c.Acquire();
        var entered=ReviewerAssemblyRetainedReaderFixture.Barrier();var release=ReviewerAssemblyRetainedReaderFixture.Barrier();byte[]? observed=null;
        using var unblock=ReviewerAssemblyRetainedReaderFixture.ReleaseOnDispose(release);
        s.BufferAllocated=b=>observed=b;
        s.ProjectionCheckpoint=async phase=>{if(phase=="BeforeProject"){entered.TrySetResult();await release.Task.ConfigureAwait(false);}};
        var first=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);await ReviewerAssemblyRetainedReaderFixture.Bounded(entered.Task);
        var queued=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);Task close=s.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);await Assert.ThrowsAsync<ObjectDisposedException>(()=>queued.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        long spent=s.Accounting.Spent;release.TrySetResult();await Assert.ThrowsAsync<ObjectDisposedException>(()=>first.WaitAsync(TimeSpan.FromSeconds(10)));await ReviewerAssemblyRetainedReaderFixture.Bounded(close);
        Assert.NotNull(observed);Assert.All(observed!,b=>Assert.Equal((byte)0,b));Assert.Equal(0,s.Accounting.Live);Assert.Equal(0,s.Accounting.Reserved);Assert.Equal(spent,s.Accounting.Spent);s.Dispose();
    }
    [Fact]
    public async Task Failed_projection_cleans_its_buffers_and_preserves_prior_success_and_spent_work()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();using var s=c.Acquire();var store=(IArtifactContentStore)s;
        byte[]? prior=await store.ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);long live=s.Accounting.Live,spent=s.Accounting.Spent;byte[]? failed=null;
        s.BufferAllocated=b=>{failed=b;throw new InvalidOperationException("InjectedFailure");};
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal("InjectedFailure",error.Message);
        Assert.All(failed!,b=>Assert.Equal((byte)0,b));Assert.Equal("synthetic\n",System.Text.Encoding.UTF8.GetString(prior!));
        Assert.Equal(live,s.Accounting.Live);Assert.Equal(1,s.Accounting.Buffers);Assert.True(s.Accounting.Spent>spent);Assert.Equal(0,s.Accounting.Reserved);
        s.BufferAllocated=null;Assert.Equal(prior,await store.ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
    }
    [Fact]
    public async Task Cancellation_precedes_closed_state_unsupported_and_bad_keys()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();var s=c.Acquire();using var token=new CancellationTokenSource();token.Cancel();s.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>((IArtifactContentStore)s).ReadAsync(default,token.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>((IArtifactContentStore)s).DeleteAsync(default,token.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>((IArtifactContentStore)s).DeleteAsync(default));
    }
    [Fact]
    public async Task Active_cancellation_during_close_wins_and_does_not_refund_spent_work()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();var s=c.Acquire();using var token=new CancellationTokenSource();
        var entered=ReviewerAssemblyRetainedReaderFixture.Barrier();var release=ReviewerAssemblyRetainedReaderFixture.Barrier();
        using var unblock=ReviewerAssemblyRetainedReaderFixture.ReleaseOnDispose(release);
        s.ProjectionCheckpoint=async phase=>{if(phase=="BeforeProject"){entered.TrySetResult();await release.Task.ConfigureAwait(false);}};
        var query=((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact,token.Token);await ReviewerAssemblyRetainedReaderFixture.Bounded(entered.Task);
        long spent=s.Accounting.Spent;var close=s.DisposeAsync().AsTask();token.Cancel();release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>query.WaitAsync(TimeSpan.FromSeconds(10)));await ReviewerAssemblyRetainedReaderFixture.Bounded(close);
        Assert.Equal(spent,s.Accounting.Spent);Assert.Equal(0,s.Accounting.Live);
    }
    [Fact]
    public async Task Sibling_sessions_share_queue_capacity_and_waiter_cancellation_releases_only_its_reservation()
    {
        var limits=ReviewerAssemblyRetainedReaderFixture.Caps;
        using var c=ReviewerAssemblyRetainedReaderFixture.Context(caps:limits with{MaximumQueuedWorkUnitsContext=limits.MaximumWorkPerQuery});
        using var first=c.Acquire();using var second=c.Acquire();using var cancellation=new CancellationTokenSource();
        var firstEntered=ReviewerAssemblyRetainedReaderFixture.Barrier();var secondEntered=ReviewerAssemblyRetainedReaderFixture.Barrier();
        var release=ReviewerAssemblyRetainedReaderFixture.Barrier();using var unblock=ReviewerAssemblyRetainedReaderFixture.ReleaseOnDispose(release);
        first.ProjectionCheckpoint=async phase=>{if(phase=="BeforePrecount"){firstEntered.TrySetResult();await release.Task.ConfigureAwait(false);}};
        second.ProjectionCheckpoint=async phase=>{if(phase=="BeforePrecount"){secondEntered.TrySetResult();await release.Task.ConfigureAwait(false);}};
        var activeFirst=((IArtifactContentStore)first).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        var activeSecond=((IArtifactContentStore)second).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        await ReviewerAssemblyRetainedReaderFixture.Bounded(Task.WhenAll(firstEntered.Task,secondEntered.Task));
        var waitingFirst=((IArtifactContentStore)first).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact,cancellation.Token);
        Assert.Equal(limits.MaximumWorkPerQuery,c.Accounting.Queued);
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)second).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.Equal("QueueCapacity",error.Message);Assert.Equal(limits.MaximumWorkPerQuery,second.Accounting.Reserved);Assert.Equal(0,second.Accounting.Queued);
        long spent=c.Accounting.Spent;cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>waitingFirst.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(spent,c.Accounting.Spent);Assert.Equal(0,c.Accounting.Queued);
        var waitingSecond=((IArtifactContentStore)second).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.Equal(limits.MaximumWorkPerQuery,c.Accounting.Queued);Assert.Equal(0,first.Accounting.Queued);
        release.TrySetResult();await ReviewerAssemblyRetainedReaderFixture.Bounded(Task.WhenAll(activeFirst,activeSecond,waitingSecond));
        Assert.Equal(0,c.Accounting.Reserved);Assert.Equal(0,c.Accounting.Queued);Assert.NotSame(activeFirst.Result,activeSecond.Result);
    }

}
