using System.Diagnostics;
using System.Runtime.Versioning;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

[SupportedOSPlatform("linux")]
public sealed class ArtifactContentInspectionConcurrencyTests
{
    private static ArtifactContentMutationContext Context() => new(ArtifactContentOperationId.New());
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "emf-inspection-concurrency-" + Guid.NewGuid().ToString("N"));
        internal ArtifactId Id { get; } = new("synthetic-delayed-write");
        internal DelayPlatform Platform { get; } = new();
        internal FileSystemArtifactContentStore Store { get; }
        internal Fixture(TimeSpan budget) => Store = new(Root, 1024, Platform, new() { MaximumDuration = budget });
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [Fact]
    public async Task Payload_flush_beyond_old_thirty_second_deadline_does_not_cancel_caller_and_revalidates()
    {
        using var f = new Fixture(TimeSpan.FromSeconds(30));
        await f.Store.ReadAsync(f.Id);
        using var caller = new CancellationTokenSource();
        var revalidated = false;
        var admissionDisposed = false;
        f.Store.InspectionCheckpoint = point =>
        {
            if (!f.Platform.Completed && point == "InspectionDisposed") admissionDisposed = true;
            if (f.Platform.Completed && point == "SqlInspection") revalidated = true;
        };
        f.Platform.Delay = async token =>
        {
            Assert.Equal(caller.Token, token);
            Assert.True(admissionDisposed);
            // An independent operation runs while this payload is waiting on I/O.
            var independent = new FileSystemArtifactContentStore(f.Root);
            await independent.CreateIfAbsentAsync(new("independent"), new byte[] { 9 }, Context());
            await Task.Delay(TimeSpan.FromSeconds(31), token);
        };
        var outcome = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context(), caller.Token);
        Assert.False(caller.IsCancellationRequested);
        Assert.True(revalidated);
        Assert.Equal(ArtifactContentMutationOutcome.Created, outcome.Outcome);
        Assert.Equal(caller.Token, f.Platform.WriteToken);
        Assert.Equal(caller.Token, f.Platform.FlushToken);
        Assert.Equal(new byte[] { 1 }, await f.Store.ReadAsync(f.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_validation_after_delayed_payload_rejects_damage_or_preserves_concurrent_winner(bool damage)
    {
        using var f = new Fixture(TimeSpan.FromMilliseconds(100));
        await f.Store.ReadAsync(f.Id);
        var original = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 0 }, Context());
        var operation = Context();
        f.Platform.Delay = async token =>
        {
            var competing = new FileSystemArtifactContentStore(f.Root);
            await competing.ReplaceIfRevisionMatchesAsync(f.Id, original.CurrentRevision!.Value, new byte[] { 9 }, Context(), token);
            if (damage)
            {
                using var connection = new SqliteConnection($"Data Source={Path.Combine(f.Root, ".content-catalog.sqlite")};Pooling=False");
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "UPDATE ContentState SET Length=17"; command.ExecuteNonQuery();
            }
            await Task.Delay(200, token);
        };
        if (damage)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.ReplaceIfRevisionMatchesAsync(f.Id, original.CurrentRevision!.Value, new byte[] { 1 }, operation));
        }
        else
        {
            Assert.Equal(ArtifactContentMutationOutcome.VersionConflict,
                (await f.Store.ReplaceIfRevisionMatchesAsync(f.Id, original.CurrentRevision!.Value, new byte[] { 1 }, operation)).Outcome);
            Assert.Equal(new byte[] { 9 }, await f.Store.ReadAsync(f.Id));
        }
    }

    [Fact]
    public async Task Fresh_validation_timeout_fails_closed_without_canceling_caller_or_publishing_receipt()
    {
        using var f = new Fixture(TimeSpan.FromMilliseconds(100));
        await f.Store.ReadAsync(f.Id);
        using var caller = new CancellationTokenSource();
        f.Platform.Delay = token => Task.Delay(200, token);
        var inspected = false;
        f.Store.InspectionCheckpoint = point =>
        {
            if (!f.Platform.Completed || point != "SqlProgress") return;
            inspected = true;
            SpendInspectionTime(TimeSpan.FromMilliseconds(150));
        };
        var operation = Context();
        await Assert.ThrowsAsync<ArtifactContentInspectionTimeoutException>(() =>
            f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, operation, caller.Token));
        Assert.True(inspected);
        Assert.False(caller.IsCancellationRequested);
        var verifier = new FileSystemArtifactContentStore(f.Root);
        Assert.Null(await verifier.GetMutationOutcomeAsync(operation.OperationId));
        Assert.Null(await verifier.ReadAsync(f.Id));
    }

    [Fact]
    public async Task Real_caller_cancellation_during_generation_flush_retains_caller_token()
    {
        using var f = new Fixture(TimeSpan.FromSeconds(30));
        await f.Store.ReadAsync(f.Id);
        using var caller = new CancellationTokenSource();
        f.Platform.Delay = token => { caller.Cancel(); return Task.Delay(1, token); };
        var operation = Context();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, operation, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Null(await new FileSystemArtifactContentStore(f.Root).GetMutationOutcomeAsync(operation.OperationId));
    }

    [Fact]
    public async Task Internal_inspection_timeout_becomes_recovery_review_instead_of_caller_cancellation()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Physical.ReadAsync(f.Id);
        var limits = new ArtifactContentInspectionLimits { MaximumDuration = TimeSpan.FromMilliseconds(100) };
        var limited = new FileSystemArtifactContentStore(Path.Combine(f.Root, "content"), inspectionLimits: limits);
        await limited.ReadAsync(f.Id);
        limited.InspectionCheckpoint = point => { if (point == "SqlProgress") SpendInspectionTime(TimeSpan.FromMilliseconds(150)); };
        await Assert.ThrowsAsync<ArtifactContentInspectionTimeoutException>(() => f.Service(physical: limited).IngestAsync(f.Draft, f.Content));
        var original = await f.IntentAsync();
        var candidate = await f.Staging.ReadAsync(original.OperationId);
        var outcome = await f.Restart(physical: limited).RecoverOperationAsync(original.OperationId);
        Assert.Equal(EMF.Core.Contracts.Ingestion.ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.Equal("RecoveryEvidenceFailure", outcome.SafeFailureCategory);
        Assert.Equal(candidate, await f.Staging.ReadAsync(original.OperationId));
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
    }

    [Fact]
    public async Task Generation_gate_wait_beyond_inspection_bound_uses_only_caller_cancellation()
    {
        using var f = new Fixture(TimeSpan.FromMilliseconds(100));
        await f.Store.ReadAsync(f.Id);
        using var caller = new CancellationTokenSource();
        var queued = new TaskCompletionSource<(string Path, CancellationToken Token, bool AdmissionDisposed)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admissionDisposed = false;
        var revalidated = false;
        f.Store.InspectionCheckpoint = point =>
        {
            Assert.False(caller.IsCancellationRequested);
            if (point == "InspectionDisposed") admissionDisposed = true;
            if (release.Task.IsCompleted && point == "SqlInspection") revalidated = true;
        };
        f.Platform.BeforeSharedAcquire = (path, token) =>
        {
            queued.SetResult((path, token, admissionDisposed));
            return release.Task.WaitAsync(token);
        };
        Task<ArtifactContentMutationResult>? pending = null;
        try
        {
            using (await f.Platform.AcquireAsync(Path.Combine(f.Root, ".content-coordination"), true, caller.Token))
            {
                pending = f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context(), caller.Token);
                await Task.WhenAny(queued.Task, pending);
                if (!queued.Task.IsCompleted) await pending; // Surface failures before the queue seam.
                var observation = await queued.Task;
                Assert.Equal(Path.Combine(f.Root, ".content-coordination"), observation.Path);
                Assert.Equal(caller.Token, observation.Token);
                Assert.True(observation.AdmissionDisposed);
                // This minimum wait exceeds the injected 100 ms inspection bound.
                // Native acquisition has not begun, so scheduling cannot consume
                // its independent 10-second coordination timeout while we hold it.
                var wait = Stopwatch.StartNew();
                await Task.Delay(200);
                Assert.True(wait.Elapsed >= TimeSpan.FromMilliseconds(100));
                Assert.False(caller.IsCancellationRequested);
                Assert.False(pending.IsCompleted);
            }
        }
        finally
        {
            // The native exclusive handle is disposed before forwarding the waiter,
            // including when an assertion fails. No scheduler deadline controls release.
            release.TrySetResult();
            if (pending is not null) await pending; // Drain the producer before fixture cleanup.
        }
        Assert.Equal(ArtifactContentMutationOutcome.Created, (await pending!).Outcome);
        Assert.True(revalidated);
        Assert.False(caller.IsCancellationRequested);
    }

    [Fact]
    public void Unrelated_thread_processing_and_scheduling_wait_do_not_spend_inspection_work()
    {
        using var budget = new ContentInspectionBudget(new() { MaximumDuration = TimeSpan.FromMilliseconds(100) }, default);
        using (budget.Inspect())
        {
            var unrelated = new Thread(() => SpendInspectionTime(TimeSpan.FromMilliseconds(200)));
            unrelated.Start(); unrelated.Join();
            budget.Check();
        }
        budget.Check();
    }

    private static void SpendInspectionTime(TimeSpan duration)
    {
        var end = LinuxContentDurability.InspectionProcessingTime() + duration;
        while (LinuxContentDurability.InspectionProcessingTime() < end) { }
    }
    private sealed class DelayPlatform : IContentStoragePlatform
    {
        private readonly IContentStoragePlatform _inner = ContentStoragePlatform.Select();
        internal Func<CancellationToken, Task>? Delay;
        internal Func<string, CancellationToken, Task>? BeforeSharedAcquire;
        internal bool Completed;
        internal CancellationToken WriteToken, FlushToken;
        public IGenerationNamespaceWatch CreateGenerationNamespaceWatch(string directory) => _inner.CreateGenerationNamespaceWatch(directory);
        public void RequirePlatform() => _inner.RequirePlatform();
        public void RequireSameFileSystem(string root, string stage) => _inner.RequireSameFileSystem(root, stage);
        public ContentSourceIdentity InspectSourceFile(string path) => _inner.InspectSourceFile(path);
        public FileStream OpenSourceFile(string path) => _inner.OpenSourceFile(path);
        public Task<IDisposable> AcquireAdmissionAsync(string root, CancellationToken token) => _inner.AcquireAdmissionAsync(root, token);
        public async Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken token)
        {
            if (!exclusive && BeforeSharedAcquire is not null) await BeforeSharedAcquire(path, token);
            return await _inner.AcquireAsync(path, exclusive, token);
        }
        public void FlushDirectory(string path, bool verify = false) => _inner.FlushDirectory(path, verify);
        public void CreatePrivateDirectory(string path) => _inner.CreatePrivateDirectory(path);
        public void ValidatePrivatePermissions(string path) => _inner.ValidatePrivatePermissions(path);
        public FileStream CreatePrivateFile(string path, bool asynchronous = false)
        {
            if (!asynchronous || Delay is null) return _inner.CreatePrivateFile(path, asynchronous);
            using (var stream = _inner.CreatePrivateFile(path)) { }
            return new DelayedFile(path, this);
        }
        private sealed class DelayedFile(string path, DelayPlatform owner)
            : FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous)
        {
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
            { owner.WriteToken = token; return base.WriteAsync(bytes, token); }
            public override async Task FlushAsync(CancellationToken token)
            {
                owner.FlushToken = token;
                await owner.Delay!(token);
                owner.Completed = true;
                await base.FlushAsync(token);
            }
        }
    }
}
