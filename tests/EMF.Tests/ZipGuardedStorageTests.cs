using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
namespace EMF.Tests;

public sealed class ZipGuardedStorageTests
{
    private sealed class Guard : IZipEffectBoundaryGuard, IDisposable
    {
        private readonly CancellationTokenSource _invalidated = new();
        public CancellationToken Invalidated => _invalidated.Token;
        public void Revoke() => _invalidated.Cancel();
        public ValueTask RevalidateAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Invalidated.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public IDisposable Accept(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Invalidated.ThrowIfCancellationRequested(); return new Acceptance(); }
        private sealed class Acceptance : IDisposable { public void Dispose() { } }
        public void Dispose() => _invalidated.Dispose();
    }
    private sealed class MissingAcceptance : IZipEffectBoundaryGuard
    {
        public CancellationToken Invalidated => default;
        public ValueTask RevalidateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
        public IDisposable Accept(CancellationToken ct = default) => null!;
    }
    private sealed class ThrowingAllocationAcceptance(int throwOn = 2) : IZipEffectBoundaryGuard
    {
        private int accepted;
        public CancellationToken Invalidated => default;
        public ValueTask RevalidateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
        public IDisposable Accept(CancellationToken ct = default) => new Release(Interlocked.Increment(ref accepted) == throwOn);
        private sealed class Release(bool fail) : IDisposable
        { public void Dispose() { if (fail) throw new InvalidOperationException("Synthetic acceptance disposal failure."); } }
    }
    private sealed class Root : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emf-zip-guard-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
    private static ArtifactContentMutationContext Context() => new(new("zip-create-operation"), new("zip-owner"));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Initiated_IO_settles_before_acceptance_failure_is_propagated(bool staging)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        async Task Io() { started.TrySetResult(); await release.Task; completed = true; }
        var guard = new ThrowingAllocationAcceptance(1);
        var pending = staging
            ? FileSystemArtifactContentStagingStore.CompleteAcceptedAsync(() => new ValueTask(Io()), guard, default).AsTask()
            : FileSystemArtifactContentStore.CompleteAcceptedAsync(() => new ValueTask(Io()), guard, default).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(completed);
    }
    [Fact]
    public async Task Acceptance_disposal_failure_after_allocation_clears_allocated_bytes()
    {
        using var root = new Root();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        var created = await store.CreateIfAbsentAsync(new("zip-object"), new byte[] { 1, 2, 3 }, Context());
        byte[]? allocated = null;
        store.BoundedReadAllocated = bytes => { allocated = bytes; Array.Fill(bytes, (byte)99); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadBoundedVersionedGuardedAsync(new("zip-object"), new(1024, 1024, created.CurrentRevision), new ThrowingAllocationAcceptance()));
        Assert.NotNull(allocated);
        Assert.All(allocated, value => Assert.Equal((byte)0, value));
    }
    [Fact]
    public async Task Missing_acceptance_denies_physical_prepare_before_file_creation()
    {
        using var root = new Root();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.PreparePhysicalCreateGuardedAsync(new("zip-object"), new byte[] { 1 }, Context(), new MissingAcceptance()));
        var generations = System.IO.Path.Combine(root.Path, ".content-generations");
        Assert.True(!Directory.Exists(generations) || Directory.GetFiles(generations).Length == 0);
    }
    [Fact]
    public async Task Missing_acceptance_denies_staging_before_temporary_file_creation()
    {
        using var root = new Root();
        var staging = new FileSystemArtifactContentStagingStore(root.Path, 1024);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staging.StageGuardedAsync(new("zip-operation"), new byte[] { 1 }, new MissingAcceptance()));
        Assert.Empty(Directory.GetFiles(root.Path, "*.preparing"));
        Assert.Empty(Directory.GetFiles(root.Path, "*.candidate"));
    }
    [Fact]
    public async Task Missing_acceptance_denies_protected_read_before_allocation()
    {
        using var root = new Root();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        var created = await store.CreateIfAbsentAsync(new("zip-object"), new byte[] { 1 }, Context());
        var allocations = 0; store.BoundedReadAllocated = _ => allocations++;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadBoundedVersionedGuardedAsync(new("zip-object"), new(1024, 1024, created.CurrentRevision), new MissingAcceptance()));
        Assert.Equal(0, allocations);
    }
    [Fact]
    public async Task Guarded_prepare_exposes_exact_guard_and_raw_prepare_has_no_capability()
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        await using (var guarded = await store.PreparePhysicalCreateGuardedAsync(new("zip-object"), new byte[] { 1 }, Context(), guard))
            Assert.Same(guard, Assert.IsAssignableFrom<IZipGuardedPreparedContentMutation>(guarded).BoundaryGuard);
        await using var raw = await store.PreparePhysicalCreateAsync(new("zip-object"), new byte[] { 1 }, Context());
        Assert.False(raw is IZipGuardedPreparedContentMutation);
    }
    [Theory]
    [InlineData("BeforeGenerationCreate", false)]
    [InlineData("BeforeGenerationRename", false)]
    [InlineData("CatalogIntegrityPrepared", true)]
    public async Task Physical_prepare_revalidates_after_wait_before_effect(string boundary, bool renamed)
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        store.Checkpoint = point => { if (point == boundary) guard.Revoke(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PreparePhysicalCreateGuardedAsync(new("zip-object"), new byte[] { 1, 2 }, Context(), guard));
        Assert.Null(await store.GetMutationOutcomeAsync(new("zip-create-operation")));
        var generations = Directory.GetFiles(System.IO.Path.Combine(root.Path, ".content-generations"));
        if (boundary == "BeforeGenerationCreate") Assert.Empty(generations);
        Assert.Equal(renamed, generations.Any(path => !path.EndsWith(".tmp", StringComparison.Ordinal)));
    }
    [Fact]
    public async Task Catalog_commit_denied_after_wait_rolls_back_receipt_and_content()
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        await using var prepared = await store.PreparePhysicalCreateGuardedAsync(new("zip-object"), new byte[] { 1 }, Context(), guard);
        store.Checkpoint = point => { if (point == "BeforeCommit") guard.Revoke(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.ExecuteAsync());
        Assert.Null(await store.GetMutationOutcomeAsync(new("zip-create-operation")));
        Assert.Null(await store.ReadAsync(new("zip-object")));
    }
    [Fact]
    public async Task Invalidation_after_commit_preserves_exact_factual_receipt()
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        await using var prepared = await store.PreparePhysicalCreateGuardedAsync(new("zip-object"), new byte[] { 1 }, Context(), guard);
        store.Checkpoint = point => { if (point == "Committed") guard.Revoke(); return Task.CompletedTask; };
        var result = await prepared.ExecuteAsync();
        Assert.Equal(result.Receipt, await store.GetMutationOutcomeAsync(new("zip-create-operation")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.RevalidateAsync().AsTask());
    }
    [Theory]
    [InlineData("ReaderSelected")]
    [InlineData("BeforeReadAllocation")]
    public async Task Protected_read_revalidates_after_wait_without_allocating_payload(string boundary)
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        var created = await store.CreateIfAbsentAsync(new("zip-object"), new byte[] { 1 }, Context());
        var allocations = 0; store.BoundedReadAllocated = _ => allocations++;
        store.Checkpoint = point => { if (point == boundary) guard.Revoke(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadBoundedVersionedGuardedAsync(new("zip-object"), new(1024, 1024, created.Receipt.CurrentRevision), guard));
        Assert.Equal(0, allocations);
    }
    [Fact]
    public async Task Revocation_after_allocation_clears_buffer_without_read_handoff()
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        var created = await store.CreateIfAbsentAsync(new("zip-object"), new byte[] { 1, 2, 3 }, Context());
        byte[]? allocated = null;
        store.BoundedReadAllocated = bytes => { allocated = bytes; guard.Revoke(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadBoundedVersionedGuardedAsync(new("zip-object"), new(1024, 1024, created.Receipt.CurrentRevision), guard));
        Assert.NotNull(allocated);
        Assert.All(allocated, value => Assert.Equal(0, value));
    }
    [Fact]
    public async Task Previously_prepared_create_is_not_permission_after_invalidation()
    {
        using var root = new Root(); using var guard = new Guard();
        var store = new FileSystemArtifactContentStore(root.Path, 1024);
        await using var prepared = await store.PreparePhysicalCreateGuardedAsync(new("zip-object"), new byte[] { 1 }, Context(), guard);
        guard.Revoke();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.ExecuteAsync());
        Assert.Null(await store.GetMutationOutcomeAsync(new("zip-create-operation")));
    }
    [Theory]
    [InlineData("BeforeCreate")]
    [InlineData("BeforeRename")]
    public async Task Candidate_staging_revalidates_after_wait_and_discards_only_local_temporary(string boundary)
    {
        using var root = new Root(); using var guard = new Guard();
        var staging = new FileSystemArtifactContentStagingStore(root.Path, 1024);
        staging.GuardedCheckpoint = point => { if (point == boundary) guard.Revoke(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => staging.StageGuardedAsync(new("zip-create-operation"), new byte[] { 1 }, guard));
        Assert.Empty(Directory.GetFiles(root.Path, "*.candidate"));
        Assert.Empty(Directory.GetFiles(root.Path, "*.preparing"));
    }
}
