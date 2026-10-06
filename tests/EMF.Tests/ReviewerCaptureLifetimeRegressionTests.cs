using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Persistence.Storage;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerCaptureLifetimeRegressionTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly TimeSpan UsabilityProbeDeadline = TimeSpan.FromMinutes(2);
        private readonly string root = Path.Combine(Path.GetTempPath(), "emf-capture-lifetime-" + Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(root, "metadata.sqlite");
        public ArtifactId Artifact { get; } = new("synthetic");
        public FileSystemArtifactContentStore Source { get; private set; } = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); Directory.CreateDirectory(f.root);
            f.Source = new(Path.Combine(f.root, "source"));
            var bytes = Encoding.UTF8.GetBytes("synthetic lifetime input");
            await f.Source.WriteAsync(f.Artifact, bytes);
            await using var c = new SqliteConnection($"Data Source={f.Database};Pooling=False");
            await c.OpenAsync();
            await using var command = c.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE Artifacts(Id TEXT PRIMARY KEY,ArtifactType TEXT,FingerprintAlgorithm TEXT,FingerprintValue TEXT);
                CREATE TABLE ArtifactMutationAuthority(ArtifactId TEXT PRIMARY KEY,ClassificationId TEXT,ClassificationRevision TEXT,IsAdopted INTEGER);
                CREATE TABLE Provenance(Id INTEGER PRIMARY KEY,ArtifactId TEXT);
                CREATE TABLE Relationships(Id INTEGER PRIMARY KEY,SourceArtifactId TEXT,TargetArtifactId TEXT);
                INSERT INTO Artifacts VALUES('synthetic','text/plain','SHA-256',$hash);
                INSERT INTO ArtifactMutationAuthority VALUES('synthetic','Confidential','classification-1',1);
                """;
            command.Parameters.AddWithValue("$hash", ReviewerRetainedValidator.Hash(bytes));
            await command.ExecuteNonQueryAsync(); return f;
        }
        public Task<ReviewerCaptureSession> Open(CancellationToken ct = default,
            Func<string, CancellationToken, Task>? checkpoint = null, int maximumWork = 4096,
            TimeSpan? deadline = null) =>
            ReviewerCaptureSession.OpenAsync(Database, OperationSnapshotId.New(), [Artifact], Source,
                new(deadline ?? TimeSpan.FromSeconds(10), MaximumWork: maximumWork), ct, checkpoint);
        public async Task AssertUsable()
        {
            // This real independent capture proves resource usability, not deadline behavior.
            // Allow loaded CI/VM scheduling and I/O without changing the ordinary test deadline.
            await using var session = await Open(deadline: UsabilityProbeDeadline);
            Assert.NotNull(await session.CaptureAsync());
        }
        public ValueTask DisposeAsync() { Directory.Delete(root, true); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task Concurrent_disposal_waits_for_capture_and_removes_interrupt_before_connection_disposal()
    {
        await using var f = await Fixture.Create();
        using var caller = new CancellationTokenSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await f.Open(checkpoint: async (phase, _) =>
        {
            if (phase == "Metadata") { reached.TrySetResult(); await release.Task; }
        });
        SqliteConnection? owned = null; var detached = false;
        session.SqliteCheckpoint = (phase, c) =>
        {
            if (phase == "Detached")
            {
                owned = c; detached = true;
                Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_exec(c.Handle!, "SELECT 1"));
            }
        };
        var capture = session.CaptureAsync(caller.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = session.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        caller.Cancel(); release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(detached); Assert.Equal(System.Data.ConnectionState.Closed, owned!.State);
        // This cancellation happens after disposal, when the local callback must be gone.
        using var after = new CancellationTokenSource();
        await using var second = await f.Open();
        await second.CaptureAsync(after.Token); await second.DisposeAsync(); after.Cancel();
        await f.AssertUsable();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_disposal_during_native_SQLite_work_waits_until_callbacks_are_detached(bool cancelActive)
    {
        await using var f = await Fixture.Create();
        await using (var c = new SqliteConnection($"Data Source={f.Database};Pooling=False"))
        {
            await c.OpenAsync(); await using var command = c.CreateCommand();
            command.CommandText = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<20000) INSERT INTO Relationships SELECT x,'other','other' FROM n";
            await command.ExecuteNonQueryAsync();
        }
        using var caller = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken effectiveCaptureToken = default;
        await using var session = await f.Open(checkpoint: (phase, ct) =>
        {
            if (phase == "Metadata") effectiveCaptureToken = ct;
            return Task.CompletedTask;
        }, maximumWork: 2000000);
        var callbacks = 0; var detached = false; SqliteConnection? owned = null;
        session.SqliteCheckpoint = (phase, c) =>
        {
            if (phase == "Progress")
            {
                callbacks++; reached.TrySetResult();
                // Bounded outer waits and their finally own release of this barrier.
                release.Wait();
            }
            else
            {
                owned = c;
                var before = callbacks;
                Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_exec(c.Handle!, "SELECT SUM(Id) FROM Relationships"));
                Assert.Equal(before, callbacks); detached = true;
            }
        };
        var capture = Task.Run(() => session.CaptureAsync(caller.Token));
        Task? disposal = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellationRegistration = effectiveCaptureToken.Register(() => cancellationObserved.TrySetResult());
            disposal = session.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
            if (cancelActive) caller.Cancel();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        Assert.IsType<SqliteException>(error.InnerException);
        await disposal!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(detached); Assert.True(callbacks > 0);
        Assert.Equal(System.Data.ConnectionState.Closed, owned!.State);
        var beforeCancellation = callbacks;
        caller.Cancel(); // The original registration must be gone after the disposal join.
        Assert.Equal(beforeCancellation, callbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_cancels_active_capture_and_all_callers_join_complete_cleanup(bool failCleanup)
    {
        await using var f = await Fixture.Create();
        using var acquisitionCaller = new CancellationTokenSource();
        using var captureCaller = new CancellationTokenSource();
        using var cleanupRelease = new ManualResetEventSlim();
        var captureReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("synthetic active disposal failure");
        var session = await f.Open(acquisitionCaller.Token, async (phase, ct) =>
        {
            if (phase != "Metadata") return;
            using var registration = ct.Register(() => cancellationObserved.TrySetResult());
            captureReached.TrySetResult();
            await captureRelease.Task;
            ct.ThrowIfCancellationRequested();
        });
        SqliteConnection? owned = null;
        var detachments = 0;
        session.SqliteCheckpoint = (phase, c) =>
        {
            if (phase != "Detached") return;
            owned = c;
            Interlocked.Increment(ref detachments);
            Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_exec(c.Handle!, "SELECT 1"));
            cleanupReached.TrySetResult();
            // Keep cleanup blocked until the test asserts its state; outer waits release in finally.
            cleanupRelease.Wait();
            if (failCleanup) throw failure;
        };
        // The blocking teardown hook must not occupy xUnit's test-control context.
        var capture = Task.Run(() => session.CaptureAsync(captureCaller.Token));
        Task? first = null, second = null;
        try
        {
            await captureReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first = session.DisposeAsync().AsTask();
            second = session.DisposeAsync().AsTask();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(acquisitionCaller.IsCancellationRequested);
            Assert.False(captureCaller.IsCancellationRequested);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            captureRelease.TrySetResult();
            await cleanupReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(capture.IsCompleted);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(System.Data.ConnectionState.Open, owned!.State);
        }
        finally
        {
            captureRelease.TrySetResult();
            cleanupRelease.Set();
        }
        if (failCleanup)
        {
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => capture));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => first!));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => second!));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask()));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
            await Task.WhenAll(first!, second!).WaitAsync(TimeSpan.FromSeconds(5));
            await session.DisposeAsync();
            await session.DisposeAsync();
        }
        Assert.Equal(1, detachments);
        Assert.Equal(System.Data.ConnectionState.Closed, owned!.State);
        acquisitionCaller.Cancel();
        captureCaller.Cancel();
        Assert.Equal(1, detachments);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.CaptureAsync());
        await f.AssertUsable();
    }

    [Fact]
    public async Task Idle_cleanup_failure_is_observed_by_disposal_without_escaping_cancellation_callback()
    {
        await using var f = await Fixture.Create(); using var caller = new CancellationTokenSource();
        var session = await f.Open(caller.Token); SqliteConnection? owned = null;
        session.SqliteCheckpoint = (phase, c) =>
        {
            if (phase == "Detached") { owned = c; throw new IOException("synthetic idle disposal failure"); }
        };
        caller.Cancel(); // Cancellation callbacks must not throw an aggregate disposal exception.
        Assert.Equal(System.Data.ConnectionState.Closed, owned!.State);
        await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.CaptureAsync());
        await f.AssertUsable();
    }

    [Fact]
    public async Task Acquisition_failure_after_native_callback_installation_releases_read_connection()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Open(maximumWork: 1));
        await f.AssertUsable();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Genuine_SQLite_error_preserves_error_code_when_cancellation_is_concurrent(bool acquisition)
    {
        await using var f = await Fixture.Create(); using var caller = new CancellationTokenSource();
        async Task Hook(string phase, CancellationToken _)
        {
            if (phase != (acquisition ? "Acquiring" : "Metadata")) return;
            await using var c = new SqliteConnection($"Data Source={f.Database};Pooling=False");
            await c.OpenAsync(); await using var command = c.CreateCommand();
            command.CommandText = "SELECT * FROM MissingLifetimeRegressionTable";
            try { await command.ExecuteScalarAsync(); }
            catch (SqliteException) { caller.Cancel(); throw; }
        }
        var error = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await using var session = await f.Open(caller.Token, Hook);
            await session.CaptureAsync();
        });
        Assert.Equal(SQLitePCL.raw.SQLITE_ERROR, error.SqliteErrorCode);
        Assert.Contains("MissingLifetimeRegressionTable", error.Message);
        await f.AssertUsable();
    }
}
