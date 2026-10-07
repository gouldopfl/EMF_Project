using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using Xunit;

namespace EMF.Tests;

public sealed class ReviewerAssemblyInputOwnershipTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromMinutes(2);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Construction_failure_or_cancellation_clears_prior_owned_material_only(bool fromEncoded, bool cancellation)
    {
        var input = ReviewerAssemblyFoundationFixture.Create();
        var caller = input.Sources[0].Content; var expected = caller.ToArray();
        var encoded = Encoding.UTF8.GetBytes(ReviewerAssemblyFoundationFixture.Golden); var original = encoded.ToArray();
        using var cancel = new CancellationTokenSource();
        var registered = new List<byte[]>(); var privateBytes = new List<byte[]>();
        void Observe(string phase, byte[] buffer)
        {
            if (phase is "CanonicalOwned" or "InputOwned") privateBytes.Add(buffer);
            if (phase != "Allocated") return;
            registered.Add(buffer);
            if (registered.Count != 2) return;
            // Earlier binary content has actually decoded before this boundary.
            Assert.Contains((byte)7, registered[0]);
            if (cancellation) cancel.Cancel(); else throw new InvalidOperationException("construction allocation fault");
        }
        void Construct()
        {
            using var unexpected = fromEncoded
                ? ReviewerAssemblyInputOwner.FromEncodedObserved(encoded, ReviewerAssemblyFoundationFixture.Limits, cancel.Token, Observe)
                : ReviewerAssemblyInputOwner.CreateObserved(input, ReviewerAssemblyFoundationFixture.Limits, cancel.Token, Observe);
        }
        if (cancellation) Assert.Throws<OperationCanceledException>(Construct);
        else Assert.Throws<InvalidOperationException>(Construct);
        Assert.Equal(2, registered.Count); Assert.NotEmpty(privateBytes);
        Assert.All(registered.Concat(privateBytes), b => Assert.All(b, x => Assert.Equal((byte)0, x)));
        Assert.Equal(expected, caller); Assert.Equal(original, encoded);
        Assert.Equal(new byte[] { 7, 8, 9 }, input.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"].Binary);
    }

    [Fact]
    public void Cancellation_in_preflight_returns_no_owner_and_never_touches_caller_buffers()
    {
        var input = ReviewerAssemblyFoundationFixture.Create(); var caller = input.Sources[0].Content;
        using var cancel = new CancellationTokenSource(); var phases = new List<string>();
        Assert.Throws<OperationCanceledException>(() => ReviewerAssemblyInputOwner.CreateObserved(input,
            ReviewerAssemblyFoundationFixture.Limits, cancel.Token, (phase, _) => { phases.Add(phase); cancel.Cancel(); }));
        Assert.Equal(new[] { "PreflightStarted" }, phases);
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), caller);
    }
    private static ReviewerAssemblyInputOwner Owner(ReviewerAssemblyFoundationLimits? limits = null) =>
        ReviewerAssemblyInputOwner.Create(ReviewerAssemblyFoundationFixture.Create(), ReviewerAssemblyDraftIdentity.Foundation,
            limits ?? ReviewerAssemblyFoundationFixture.Limits);

    [Fact]
    public void Caller_object_graph_and_encoded_buffer_are_never_private_backing_memory()
    {
        var input = ReviewerAssemblyFoundationFixture.Create();
        var originalSource = input.Sources[0].Content;
        using var owner = ReviewerAssemblyInputOwner.Create(input, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits);
        originalSource[0] = 0;
        input.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"].Binary![0] = 0;
        input.Artifacts[0].Rows[0].Metadata.Clear();
        input.Members.Rows[0] = input.Members.Rows[0] with { ArtifactId = "changed" };
        using var child = owner.Acquire(); var result = child.ReadCopy();
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), result.Sources[0].Content);
        Assert.Equal(new byte[] { 7, 8, 9 }, result.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"].Binary);
        Assert.Equal("artifact-1", result.Members.Rows[0].ArtifactId);
        Assert.Equal(ReviewerAssemblyFoundationFixture.GoldenSha256, owner.CanonicalSha256);
        var encoded = Encoding.UTF8.GetBytes(ReviewerAssemblyFoundationFixture.Golden);
        using var decodedOwner = ReviewerAssemblyInputOwner.FromEncoded(encoded, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits);
        Array.Fill(encoded, (byte)0);
        using var decodedChild = decodedOwner.Acquire();
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), decodedChild.ReadCopy().Sources[0].Content);
    }

    [Fact]
    public void Deep_mutation_isolated_between_leases_and_later_reads_in_same_lease()
    {
        using var owner = Owner(); using var first = owner.Acquire(); using var second = owner.Acquire();
        var a = first.ReadCopy(); var b = second.ReadCopy();
        a.Sources[0].Content[0] = 0;
        a.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"].Binary![0] = 0;
        a.Artifacts[0].Rows[0].Metadata["z"].Properties!["values"].Items![0] = ReviewerAssemblyValue.String("changed");
        a.Conditions.Rows[0] = a.Conditions.Rows[0] with { Name = "changed" };
        a.Preparation.SelectedPageSequence[0] = 99;
        a.Provenance[0].Rows[0].Properties["added"] = ReviewerAssemblyValue.String("changed");
        a.Deferred[0] = a.Deferred[0] with { Kind = ReviewerAssemblyReceiptKind.Empty };
        var later = first.ReadCopy();
        Assert.Equal(ReviewerAssemblyFoundationFixture.Golden, Encoding.UTF8.GetString(ReviewerAssemblyCaptureRepresentation.Encode(b, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits)));
        Assert.Equal(ReviewerAssemblyFoundationFixture.Golden, Encoding.UTF8.GetString(ReviewerAssemblyCaptureRepresentation.Encode(later, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits)));
        Assert.NotSame(b.Sources[0].Content, later.Sources[0].Content);
    }

    [Fact]
    public void Registry_clears_removed_replaced_and_aliased_owned_buffers_but_not_foreign_ones()
    {
        var input = ReviewerAssemblyFoundationFixture.Create();
        var provider = input.Sources[0].Content; var providerExpected = provider.ToArray();
        using var owner = ReviewerAssemblyInputOwner.Create(input, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits);
        var first = owner.Acquire(); using var second = owner.Acquire();
        var a = first.ReadCopy(); var b = second.ReadCopy();
        var removed = a.Sources[0].Content;
        var nested = a.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"].Binary!;
        var foreign = new byte[] { 99, 98, 97 };
        a.Sources[0] = a.Sources[0] with { Content = foreign };
        a.Artifacts[0].Rows[0].Metadata["alias"] = new(ReviewerAssemblyValueKind.Binary, null, null, null, null, removed, null, null);
        a.Artifacts[0].Rows[0].Metadata["foreign"] = new(ReviewerAssemblyValueKind.Binary, null, null, null, null, foreign, null, null);
        a.Artifacts[0].Rows[0].Metadata.Remove("z");
        Assert.Equal(providerExpected, removed); Assert.Contains((byte)7, nested);
        first.Dispose(); first.Dispose();
        Assert.All(removed, x => Assert.Equal((byte)0, x)); Assert.All(nested, x => Assert.Equal((byte)0, x));
        Assert.Equal(new byte[] { 99, 98, 97 }, foreign);
        Assert.Equal(providerExpected, provider); Assert.Equal(providerExpected, b.Sources[0].Content);
        Assert.Throws<ObjectDisposedException>(() => first.ReadCopy());
    }

    [Fact]
    public void Closing_parent_defers_private_cleanup_and_existing_children_remain_valid()
    {
        var owner = Owner(); var child = owner.Acquire();
        owner.Dispose(); owner.Dispose();
        Assert.False(owner.Accounting.PrivateReleased);
        Assert.Throws<ObjectDisposedException>(() => owner.Acquire());
        var copy = child.ReadCopy(); Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), copy.Sources[0].Content);
        child.Dispose(); Assert.True(owner.Accounting.PrivateReleased);
        Assert.Equal(0L, owner.Accounting.LiveDetached); Assert.Equal(0, owner.Accounting.Children);
        Assert.All(copy.Sources[0].Content, x => Assert.Equal((byte)0, x));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Cancellation_or_fault_after_registered_allocation_clears_only_owned_buffers(bool cancellation, int boundary)
    {
        using var owner = Owner(); using var child = owner.Acquire();
        using var cancel = new CancellationTokenSource();
        var buffers = new List<byte[]>(); var initial = owner.Accounting;
        owner.Checkpoint = (_, bytes) =>
        {
            buffers.Add(bytes);
            if (buffers.Count != boundary) return;
            if (cancellation) cancel.Cancel(); else throw new InvalidOperationException("synthetic allocation fault");
        };
        if (cancellation) Assert.Throws<OperationCanceledException>(() => child.ReadCopy(cancel.Token));
        else Assert.Throws<InvalidOperationException>(() => child.ReadCopy());
        Assert.NotEmpty(buffers); Assert.All(buffers, b => Assert.All(b, x => Assert.Equal((byte)0, x)));
        Assert.Equal(initial.LiveDetached, owner.Accounting.LiveDetached);
        Assert.True(owner.Accounting.CopyWork > initial.CopyWork);
        owner.Checkpoint = null;
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), child.ReadCopy().Sources[0].Content);
    }

    [Fact]
    public void Pre_cancelled_acquisition_and_read_do_not_reserve_or_clear_caller_data()
    {
        var input = ReviewerAssemblyFoundationFixture.Create(); var provider = input.Sources[0].Content;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ReviewerAssemblyInputOwner.Create(input, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits, cancelled.Token));
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), provider);
        using var owner = Owner(); var initial = owner.Accounting;
        Assert.Throws<OperationCanceledException>(() => owner.Acquire(cancelled.Token)); Assert.Equal(initial, owner.Accounting);
        using var child = owner.Acquire(); var before = owner.Accounting;
        Assert.Throws<OperationCanceledException>(() => child.ReadCopy(cancelled.Token)); Assert.Equal(before, owner.Accounting);
    }

    [Fact]
    public async Task Acquire_and_parent_dispose_race_has_no_shared_buffer_lifetime()
    {
        var owner = Owner(); using var start = new ManualResetEventSlim();
        var acquire = Task.Run(() =>
        {
            Assert.True(start.Wait(Watchdog));
            try { return owner.Acquire(); } catch (ObjectDisposedException) { return null; }
        });
        var close = Task.Run(() => { Assert.True(start.Wait(Watchdog)); owner.Dispose(); });
        start.Set(); await Task.WhenAll(acquire, close).WaitAsync(Watchdog);
        var child = await acquire;
        if (child is not null) { Assert.NotEmpty(child.ReadCopy().Sources[0].Content); child.Dispose(); }
        Assert.True(owner.Accounting.PrivateReleased); Assert.Equal(0L, owner.Accounting.LiveDetached);
        Assert.Throws<ObjectDisposedException>(() => owner.Acquire());
    }

    [Fact]
    public async Task Concurrent_children_and_read_dispose_race_are_bounded_and_isolated()
    {
        using var owner = Owner(); using var a = owner.Acquire(); using var b = owner.Acquire();
        using var start = new ManualResetEventSlim();
        var copiesTask = Task.WhenAll(Task.Run(() => { Assert.True(start.Wait(Watchdog)); return a.ReadCopy(); }),
            Task.Run(() => { Assert.True(start.Wait(Watchdog)); return b.ReadCopy(); }));
        start.Set(); var copies = await copiesTask.WaitAsync(Watchdog);
        a.Dispose(); Assert.All(copies[0].Sources[0].Content, x => Assert.Equal((byte)0, x));
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), copies[1].Sources[0].Content);
        using var raceStart = new ManualResetEventSlim();
        var read = Task.Run(() => { Assert.True(raceStart.Wait(Watchdog)); try { return b.ReadCopy(); } catch (ObjectDisposedException) { return null; } });
        var dispose = Task.Run(() => { Assert.True(raceStart.Wait(Watchdog)); b.Dispose(); });
        raceStart.Set();
        await Task.WhenAll(read, dispose).WaitAsync(Watchdog);
        var result = await read; if (result is not null) Assert.All(result.Sources[0].Content, x => Assert.Equal((byte)0, x));
        Assert.Equal(0L, owner.Accounting.LiveDetached);
    }

    [Fact]
    public void Live_bytes_lease_counts_and_repeated_decoding_work_have_separate_limits()
    {
        var encodedLength = Encoding.UTF8.GetByteCount(ReviewerAssemblyFoundationFixture.Golden);
        var charge = ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(encodedLength);
        var limits = ReviewerAssemblyFoundationFixture.Limits with { MaximumDetachedBytes = 256 + charge, MaximumLeases = 1 };
        using var owner = Owner(limits); var child = owner.Acquire();
        Assert.Throws<InvalidDataException>(() => owner.Acquire());
        var copy = child.ReadCopy(); Assert.Equal(256 + charge, owner.Accounting.LiveDetached);
        Assert.Throws<InvalidDataException>(() => child.ReadCopy());
        var spent = owner.Accounting.CopyWork;
        child.Dispose(); Assert.Equal(0L, owner.Accounting.LiveDetached); Assert.Equal(spent, owner.Accounting.CopyWork);
        using var next = owner.Acquire(); Assert.NotEmpty(next.ReadCopy().Sources[0].Content);
        Assert.True(owner.Accounting.CopyWork > spent); Assert.All(copy.Sources[0].Content, x => Assert.Equal((byte)0, x));
        using var smaller = Owner(limits with { MaximumDetachedBytes = 256 + charge - 1 }); using var smallChild = smaller.Acquire();
        Assert.Throws<InvalidDataException>(() => smallChild.ReadCopy()); Assert.Equal(256L, smaller.Accounting.LiveDetached);
    }

    [Fact]
    public void Cumulative_copy_work_remains_spent_after_release_and_combined_work_is_preflighted()
    {
        int size = Encoding.UTF8.GetByteCount(ReviewerAssemblyFoundationFixture.Golden);
        var charge = ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(size);
        long initial = checked(ReviewerAssemblyCaptureRepresentation.ConstructionWork(size) + charge);
        var limits = ReviewerAssemblyFoundationFixture.Limits with { MaximumEncodedBytes = size, MaximumCopyWork = initial + 256 + charge };
        using var owner = Owner(limits); var child = owner.Acquire(); child.ReadCopy(); child.Dispose();
        Assert.Equal(limits.MaximumCopyWork, owner.Accounting.CopyWork); Assert.Equal(0L, owner.Accounting.LiveDetached);
        Assert.Throws<InvalidDataException>(() => owner.Acquire());
        var input = ReviewerAssemblyFoundationFixture.Create();
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyInputOwner.Create(input, ReviewerAssemblyDraftIdentity.Foundation, limits with { MaximumCopyWork = initial - 1 }));
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic\n"), input.Sources[0].Content);
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyInputOwner.Create(input, ReviewerAssemblyDraftIdentity.Foundation, limits with { MaximumPrivateBytes = charge - 1 }));
    }
}
