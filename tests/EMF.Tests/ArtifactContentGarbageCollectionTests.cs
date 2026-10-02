using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed partial class ArtifactContentGarbageCollectionTests
{
    private static ArtifactContentMutationContext Context() => new(ArtifactContentOperationId.New());
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "emf-gc-" + Guid.NewGuid().ToString("N"));
        public ArtifactId Id { get; } = new("synthetic");
        public FileSystemArtifactContentStore Store { get; }
        public string Generations => Path.Combine(Root, ".content-generations");
        public Fixture() => Store = new(Root);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            var workspace = FileSystemArtifactContentMigration.WorkspaceFor(Root);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task BoundedSweepPreservesCurrentReceiptsAndTombstoneRecreation()
    {
        using var f = new Fixture();
        var initialContext = Context();
        var first = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, initialContext);
        for (var i = 0; i < 8; i++) await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        var current = await f.Store.ReadVersionedAsync(f.Id);
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        string? cursor = null;
        var count = 0;
        do
        {
            var result = await gc.CollectAsync(new(MaxEntriesSelected: 2, MaxFilesReclaimed: 1, Continuation: cursor));
            Assert.InRange(result.EntriesSelected, 0, 2);
            Assert.InRange(result.FilesReclaimed, 0, 1);
            count += result.FilesReclaimed;
            cursor = result.Continuation;
        } while (cursor is not null);
        Assert.Equal(8, count);
        Assert.Single(Directory.GetFiles(f.Generations));
        Assert.Equal(current!.Revision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(first.Receipt, await f.Store.GetMutationOutcomeAsync(initialContext.OperationId));
        await f.Store.DeleteAsync(f.Id);
        Assert.Equal(1, (await gc.CollectAsync()).FilesReclaimed);
        Assert.Null(await new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(f.Id));
        var recreated = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context());
        Assert.NotEqual(first.CurrentRevision, recreated.CurrentRevision);
    }

    [Theory]
    [InlineData("BeforeUnlink", 0)]
    [InlineData("Unlinked", 1)]
    [InlineData("BeforeDirectoryFlush", 3)]
    public async Task InterruptedBatchRetriesWithoutChangingCatalog(string point, int missing)
    {
        using var f = new Fixture();
        for (var i = 0; i < 4; i++) await f.Store.WriteAsync(f.Id, new byte[] { (byte)i });
        var before = await f.Store.ReadVersionedAsync(f.Id);
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root)
        { Checkpoint = p => p == point ? throw new IOException("Synthetic GC interruption") : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => gc.CollectAsync());
        Assert.Equal(4 - missing, Directory.GetFiles(f.Generations).Length);
        gc.Checkpoint = null;
        await gc.CollectAsync();
        Assert.Single(Directory.GetFiles(f.Generations));
        Assert.Equal(before!.Revision, (await new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(f.Id))!.Revision);
    }

    [Fact]
    public async Task LiveProducerCandidateIsProtectedUntilOutcomeResolution()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Checkpoint = async p => { if (p == "CandidateDurable") { reached.SetResult(); await release.Task; } };
        var producer = f.Store.WriteAsync(f.Id, new byte[] { 2 });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var collection = Task.Run(() => gc.CollectAsync());
        try
        {
            await Task.Delay(150);
            Assert.False(collection.IsCompleted);
            Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
        }
        finally { release.TrySetResult(); }
        await producer;
        await collection;
        Assert.Single(Directory.GetFiles(f.Generations));
        Assert.Equal(new byte[] { 2 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
    }

    [Fact]
    public async Task ExclusiveCatalogWindowWaitsForReaderAndBlocksNewReaders()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Checkpoint = async p => { if (p == "ReaderSelected") { selected.SetResult(); await releaseReader.Task; } };
        var reader = f.Store.ReadVersionedAsync(f.Id);
        await selected.Task;
        var gated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rechecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root)
        {
            Checkpoint = async p =>
            {
                if (p == "ExclusiveGateAcquired") gated.TrySetResult();
                if (p == "EligibilityRechecked") { rechecked.TrySetResult(); await releaseGc.Task; }
            }
        };
        var collection = Task.Run(() => gc.CollectAsync());
        Task<ArtifactContentSnapshot?>? laterReader = null;
        try
        {
            await gated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            Assert.False(rechecked.Task.IsCompleted);
            releaseReader.SetResult();
            Assert.Equal(new byte[] { 1 }, (await reader)!.Content);
            await rechecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            laterReader = Task.Run(() => new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(f.Id));
            await Task.Delay(100);
            Assert.False(laterReader.IsCompleted);
        }
        finally { releaseReader.TrySetResult(); releaseGc.TrySetResult(); }
        await collection;
        Assert.NotNull(await laterReader!);
        f.Store.Checkpoint = null;
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        Assert.Equal(1, (await gc.CollectAsync()).FilesReclaimed);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("directory")]
    [InlineData("symlink")]
    [InlineData("permissions")]
    [InlineData("fifo")]
    [InlineData("hardlink")]
    [InlineData("currentMissing")]
    [InlineData("schema")]
    public async Task DamageFailsBeforeAnyUnlink(string damage)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var old = Directory.GetFiles(f.Generations).Single();
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        var bad = Path.Combine(f.Generations, Guid.NewGuid().ToString("N"));
        switch (damage)
        {
            case "unknown": await File.WriteAllTextAsync(Path.Combine(f.Generations, "unknown"), "x"); break;
            case "directory": Directory.CreateDirectory(bad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); break;
            case "symlink": File.CreateSymbolicLink(bad, old); break;
            case "permissions": File.SetUnixFileMode(old, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead); break;
            case "currentMissing": File.Delete(Directory.GetFiles(f.Generations).Single(p => p != old)); break;
            case "fifo":
            case "hardlink":
                var info = new System.Diagnostics.ProcessStartInfo(damage == "fifo" ? "mkfifo" : "ln") { UseShellExecute = false };
                if (damage == "hardlink") info.ArgumentList.Add(old);
                info.ArgumentList.Add(bad);
                using (var process = System.Diagnostics.Process.Start(info)!) { await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); }
                break;
            case "schema":
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(f.Root, ".content-catalog.sqlite")};Pooling=False"))
                { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery(); }
                break;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync());
        Assert.True(File.Exists(old));
    }

    [Fact]
    public async Task MigrationOriginsRemainProtectedAfterReplacementAndReopen()
    {
        using var f = new Fixture();
        var platform = ContentStoragePlatform.Select();
        platform.CreatePrivateDirectory(f.Root);
        using (var file = platform.CreatePrivateFile(Path.Combine(f.Root, f.Id.Value))) { file.WriteByte(1); file.Flush(true); }
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, new(true));
        var imported = Directory.GetFiles(f.Generations).Single();
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        await f.Store.WriteAsync(f.Id, new byte[] { 3 });
        Assert.Equal(1, (await new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync()).FilesReclaimed);
        Assert.True(File.Exists(imported));
        Assert.Equal(new byte[] { 3 }, (await new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(f.Id))!.Content);
    }

    [Fact]
    public async Task PartialCandidateAndDurableOrphanAreReclaimableAfterProducerFailure()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        foreach (var point in new[] { "BeforeGenerationRename", "CandidateDurable" })
        {
            f.Store.Checkpoint = p => p == point ? throw new IOException("Synthetic producer failure") : Task.CompletedTask;
            await Assert.ThrowsAsync<IOException>(() => f.Store.WriteAsync(f.Id, new byte[] { 9 }));
        }
        Assert.Equal(2, (await new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync()).FilesReclaimed);
        Assert.Single(Directory.GetFiles(f.Generations));
    }
    [Theory]
    [InlineData("BeforeUnlink")]
    [InlineData("Unlinked")]
    [InlineData("BeforeDirectoryFlush")]
    [InlineData("CandidateDurable")]
    public async Task ProcessDeathReleasesCoordinationAndRetryReconciles(string point)
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        var before = await f.Store.ReadVersionedAsync(f.Id);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-allowed"), f.Root);
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(ArtifactContentGarbageCollectionTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ArtifactContentGarbageCollectionTests.GarbageCollectionProcessWorker");
        info.Environment["EMF_GC_CHILD_ROOT"] = f.Root;
        info.Environment["EMF_GC_CHILD_POINT"] = point;
        info.Environment["EMF_AZURE_OPENAI_LIVE"] = "false";
        info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false";
        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(f.Root, "worker-ready")))
            {
                if (process.HasExited) throw new Exception(await output + await error);
                if (elapsed.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Synthetic GC worker did not start.");
                await Task.Delay(20);
            }
            if (point == "BeforeUnlink") Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync(cancellationToken: timeout.Token));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync();
            Assert.Single(Directory.GetFiles(f.Generations));
            Assert.Equal(before!.Revision, (await new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(f.Id))!.Revision);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await output;
            await error;
        }
    }

    [Fact]
    public async Task GarbageCollectionProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable("EMF_GC_CHILD_ROOT");
        if (root is null) return;
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "emf-gc-"), root);
        Assert.Equal(root, File.ReadAllText(Path.Combine(root, "worker-allowed")));
        var point = Environment.GetEnvironmentVariable("EMF_GC_CHILD_POINT");
        async Task Pause(string actual)
        {
            if (actual != point) return;
            await File.WriteAllTextAsync(Path.Combine(root, "worker-ready"), "ready");
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        if (point == "ContinuationReady")
        {
            using var gc = new FileSystemArtifactContentGarbageCollector(root);
            var result = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
            await File.WriteAllTextAsync(Path.Combine(root, "worker-token"), result.Continuation);
            await Pause("ContinuationReady");
        }
        else if (point == "CandidateDurable")
        {
            var producer = new FileSystemArtifactContentStore(root) { Checkpoint = Pause };
            await producer.WriteAsync(new("synthetic"), new byte[] { 9 });
        }
        else await new FileSystemArtifactContentGarbageCollector(root) { Checkpoint = Pause }.CollectAsync();
    }

    [Fact]
    public async Task FlushFailureDoesNotAcknowledgeSuccessAndNoOpRetryFlushesAgain()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        var platform = new FailingFlushPlatform();
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform)
        { Checkpoint = p => { if (p == "BeforeDirectoryFlush") platform.Fail = true; return Task.CompletedTask; } };
        await Assert.ThrowsAsync<IOException>(() => gc.CollectAsync());
        Assert.Single(Directory.GetFiles(f.Generations));
        platform.Fail = false;
        gc.Checkpoint = null;
        var count = platform.GenerationFlushes;
        Assert.Equal(0, (await gc.CollectAsync()).FilesReclaimed);
        Assert.True(platform.GenerationFlushes > count);
        Assert.Equal(new byte[] { 2 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
    }

    private sealed class FailingFlushPlatform : IContentStoragePlatform
    {
        private readonly IContentStoragePlatform _inner = ContentStoragePlatform.Select();
        public bool Fail { get; set; }
        public int GenerationFlushes { get; private set; }
        public void RequirePlatform() => _inner.RequirePlatform();
        public void RequireSameFileSystem(string root, string parent) => _inner.RequireSameFileSystem(root, parent);
        public ContentSourceIdentity InspectSourceFile(string path) => _inner.InspectSourceFile(path);
        public IGenerationNamespaceWatch CreateGenerationNamespaceWatch(string directory) => _inner.CreateGenerationNamespaceWatch(directory);
        public FileStream OpenSourceFile(string path) => _inner.OpenSourceFile(path);
        public Task<IDisposable> AcquireAdmissionAsync(string root, CancellationToken ct) => _inner.AcquireAdmissionAsync(root, ct);
        public Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken ct) => _inner.AcquireAsync(path, exclusive, ct);
        public void FlushDirectory(string path, bool verifyFileSystem = false)
        {
            if (Path.GetFileName(path) == ".content-generations")
            {
                GenerationFlushes++;
                if (Fail) throw new IOException("Synthetic directory flush failure");
            }
            _inner.FlushDirectory(path, verifyFileSystem);
        }
        public void CreatePrivateDirectory(string path) => _inner.CreatePrivateDirectory(path);
        public FileStream CreatePrivateFile(string path, bool asynchronous = false) => _inner.CreatePrivateFile(path, asynchronous);
        public void ValidatePrivatePermissions(string path) => _inner.ValidatePrivatePermissions(path);
    }

    [Fact]
    public async Task InventoryAndContinuationBoundsRejectBeforeDeletion()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gc.CollectAsync(new(MaxNamespaceEntries: 0)));
        var inspection = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        Assert.False(inspection.ValidationComplete);
        Assert.Equal(0, inspection.FilesReclaimed);
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
        var reclaimed = 0;
        do
        {
            inspection = await gc.CollectAsync(new(MaxNamespaceEntries: 1, Continuation: inspection.Continuation));
            reclaimed += inspection.FilesReclaimed;
        } while (!inspection.SweepComplete);
        Assert.Equal(1, reclaimed);
    }

    [Fact]
    public async Task MutationBeforeExclusiveRecheckProtectsNewCurrentGeneration()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root)
        { Checkpoint = async p => { if (p == "BeforeExclusiveGate") { reached.SetResult(); await release.Task; } } };
        var collection = gc.CollectAsync();
        await reached.Task;
        try { await f.Store.WriteAsync(f.Id, new byte[] { 2 }); }
        finally { release.TrySetResult(); }
        Assert.Equal(1, (await collection).FilesReclaimed);
        Assert.Equal(new byte[] { 2 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("provenance")]
    [InlineData("evidence")]
    public async Task MigrationDamageStopsReclamation(string damage)
    {
        using var f = new Fixture();
        var platform = ContentStoragePlatform.Select();
        platform.CreatePrivateDirectory(f.Root);
        var source = Path.Combine(f.Root, f.Id.Value);
        using (var file = platform.CreatePrivateFile(source)) { file.WriteByte(1); file.Flush(true); }
        var migration = new FileSystemArtifactContentMigration();
        if (damage == "incomplete")
        {
            migration.Checkpoint = p => p == "GenerationDurable" ? throw new IOException("Synthetic incomplete migration") : Task.CompletedTask;
            await Assert.ThrowsAsync<IOException>(() => migration.MigrateAsync(f.Root, new(true)));
            await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync());
            Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(source));
            return;
        }
        var result = await migration.MigrateAsync(f.Root, new(true));
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        await f.Store.WriteAsync(f.Id, new byte[] { 3 });
        var before = Directory.GetFiles(f.Generations).Order().ToArray();
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        // First admit without reclaiming an entry, then damage evidence: each GC
        // invocation still revalidates it before destructive work.
        var inspection = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        Assert.False(inspection.ValidationComplete);
        Assert.Equal(0, inspection.FilesReclaimed);
        if (damage == "evidence") await File.WriteAllBytesAsync(Path.Combine(result.RetainedDirectory, f.Id.Value), new byte[] { 9 });
        else
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(f.Root, ".content-catalog.sqlite")};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ContentMigrationArtifacts SET Status='Imported'";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => gc.CollectAsync());
        Assert.Equal(before, Directory.GetFiles(f.Generations).Order().ToArray());
    }

}
