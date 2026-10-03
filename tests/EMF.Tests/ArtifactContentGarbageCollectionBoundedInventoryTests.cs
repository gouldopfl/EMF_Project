using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed partial class ArtifactContentGarbageCollectionTests
{
    private static void AddSyntheticGeneration(Fixture f, string name)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        using var file = new FileStream(Path.Combine(f.Generations, name), new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        file.WriteByte(9);
    }

    [Fact]
    public async Task BoundedInventoryContinuesBeyondDefaultBudgetWithoutRaisingIt()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        for (var i = 0; i < 100001; i++) AddSyntheticGeneration(f, i.ToString("x32"));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var first = await gc.CollectAsync();
        Assert.Equal(100000, first.EntriesInspected);
        Assert.False(first.ValidationComplete);
        Assert.Equal(0, first.FilesReclaimed);
        var next = await gc.CollectAsync(new(MaxEntriesSelected: 10000, MaxFilesReclaimed: 10000, Continuation: first.Continuation));
        Assert.Equal(2, next.EntriesInspected);
        Assert.True(next.ValidationComplete);
        Assert.Equal(10000, next.FilesReclaimed);
        var reclaimed = next.FilesReclaimed;
        while (!next.SweepComplete)
        {
            next = await gc.CollectAsync(new(MaxEntriesSelected: 10000, MaxFilesReclaimed: 10000, Continuation: next.Continuation));
            Assert.Equal(0, next.EntriesInspected);
            reclaimed += next.FilesReclaimed;
        }
        Assert.Equal(100001, reclaimed);
        Assert.Single(Directory.GetFiles(f.Generations));
    }

    [Fact]
    public async Task BoundedInventoryProtectedBatchesDoNotStarveLaterCandidate()
    {
        using var f = new Fixture();
        for (var i = 0; i < 8; i++) await f.Store.WriteAsync(new ArtifactId("protected" + i), new byte[] { 1 });
        var candidate = new string('f', 32) + ".tmp";
        AddSyntheticGeneration(f, candidate);
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        string? token = null;
        var calls = 0;
        ArtifactContentGarbageCollectionResult result;
        do
        {
            result = await gc.CollectAsync(new(MaxNamespaceEntries: 2, MaxEntriesSelected: 2, Continuation: token));
            Assert.InRange(result.EntriesInspected, 0, 2);
            Assert.InRange(result.EntriesSelected, 0, 2);
            if (!result.ValidationComplete) Assert.Equal(0, result.FilesReclaimed);
            token = result.Continuation;
            Assert.True(++calls < 20);
        } while (!result.SweepComplete);
        Assert.False(File.Exists(Path.Combine(f.Generations, candidate)));
        Assert.Equal(8, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventoryUnsafeLaterBatchPreventsEntireSweepReclamation()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        for (var i = 0; i < 12; i++) AddSyntheticGeneration(f, i.ToString("x32"));
        // Damage the last synthetic orphan, not the current generation (which
        // catalog validation must reject before bounded enumeration starts).
        // Native enumeration order can place the current generation last.
        var syntheticNames = Enumerable.Range(0, 12).Select(i => i.ToString("x32")).ToHashSet(StringComparer.Ordinal);
        var last = Directory.EnumerateFileSystemEntries(f.Generations)
            .Last(path => syntheticNames.Contains(Path.GetFileName(path)));
        File.SetUnixFileMode(last, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        string? token = null;
        var calls = 0;
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            while (true)
            {
                var result = await gc.CollectAsync(new(MaxNamespaceEntries: 2, Continuation: token));
                Assert.Equal(0, result.FilesReclaimed);
                token = result.Continuation;
                calls++;
            }
        });
        Assert.True(calls >= 3);
        Assert.Equal(13, Directory.GetFiles(f.Generations).Length);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("metadata")]
    public async Task BoundedInventoryMutationDuringValidationInvalidatesSweep(string mutation)
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var name = new string('0', 32);
        AddSyntheticGeneration(f, name);
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var changed = false;
        gc.Checkpoint = p =>
        {
            if (p != "NamespaceEntryValidated" || changed) return Task.CompletedTask;
            changed = true;
            var path = Path.Combine(f.Generations, name);
            if (mutation == "create") AddSyntheticGeneration(f, new string('f', 32));
            if (mutation == "delete") File.Delete(path);
            if (mutation == "rename") File.Move(path, Path.Combine(f.Generations, new string('e', 32)));
            if (mutation == "metadata") File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<IOException>(() => gc.CollectAsync(new(MaxNamespaceEntries: 1)));
        gc.Checkpoint = null;
        Assert.Equal(new byte[] { 1 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
        Assert.Equal(mutation == "create" ? 3 : mutation == "delete" ? 1 : 2, Directory.GetFiles(f.Generations).Length);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("overflow")]
    [InlineData("metadata")]
    public async Task BoundedInventoryMutationBetweenCallsRestartsBeforeDeletion(string mutation)
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        var platform = new NamespaceWatchPlatform();
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform);
        var first = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        if (mutation == "create") AddSyntheticGeneration(f, new string('f', 32));
        if (mutation == "metadata") File.SetLastWriteTimeUtc(Path.Combine(f.Generations, new string('0', 32)), DateTime.UtcNow.AddHours(-1));
        if (mutation == "overflow") platform.LastWatch!.SimulateOverflow();
        var next = await gc.CollectAsync(new(MaxNamespaceEntries: 1, Continuation: first.Continuation));
        Assert.True(next.ValidationRestarted);
        Assert.NotEqual(first.Continuation, next.Continuation);
        Assert.Equal(1, next.EntriesInspected);
        Assert.Equal(0, next.FilesReclaimed);
        await Assert.ThrowsAsync<ArgumentException>(() => gc.CollectAsync(new(Continuation: first.Continuation)));
    }

    [Theory]
    [InlineData("move")]
    [InlineData("replace")]
    public async Task BoundedInventoryDirectoryWatchLossCannotAuthorizeDeletion(string mutation)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var first = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        var moved = f.Generations + "-moved";
        Directory.Move(f.Generations, moved);
        try
        {
            if (mutation == "replace") Directory.CreateDirectory(f.Generations, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await Assert.ThrowsAnyAsync<Exception>(() => gc.CollectAsync(new(Continuation: first.Continuation)));
            Assert.Equal(2, Directory.GetFiles(moved).Length);
        }
        finally
        {
            if (Directory.Exists(f.Generations)) Directory.Delete(f.Generations);
            Directory.Move(moved, f.Generations);
        }
    }

    [Fact]
    public async Task BoundedInventoryDisposedAndForeignSessionTokensCannotResume()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var first = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        using (var foreign = new FileSystemArtifactContentGarbageCollector(f.Root))
            await Assert.ThrowsAsync<ArgumentException>(() => foreign.CollectAsync(new(Continuation: first.Continuation)));
        gc.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gc.CollectAsync(new(Continuation: first.Continuation)));
        using var restarted = new FileSystemArtifactContentGarbageCollector(f.Root);
        await Assert.ThrowsAsync<ArgumentException>(() => restarted.CollectAsync(new(Continuation: first.Continuation)));
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventoryMutationAfterCompleteValidationFailsBeforeUnlink()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root)
        {
            Checkpoint = p =>
            {
                if (p == "EligibilityRechecked") AddSyntheticGeneration(f, new string('f', 32));
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsAsync<IOException>(() => gc.CollectAsync());
        Assert.Equal(3, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventorySmallNamespaceCompletesInOneCall()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var result = await gc.CollectAsync();
        Assert.True(result.ValidationComplete);
        Assert.True(result.SweepComplete);
        Assert.Null(result.Continuation);
        Assert.Equal(2, result.EntriesInspected);
        Assert.Equal(1, result.FilesReclaimed);
    }
    [Fact]
    public async Task BoundedInventoryWatchPrecedesFirstEnumerationAndDetectsTransientEntry()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root)
        {
            Checkpoint = p =>
            {
                if (p == "NamespaceWatchEstablished")
                {
                    AddSyntheticGeneration(f, new string('f', 32));
                    File.Delete(Path.Combine(f.Generations, new string('f', 32)));
                }
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsAsync<IOException>(() => gc.CollectAsync());
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventoryOverflowAfterValidationDiscardsDeletionAuthority()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('f', 32) + ".tmp");
        var platform = new NamespaceWatchPlatform();
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform);
        var first = await gc.CollectAsync(new(MaxEntriesSelected: 1));
        Assert.True(first.ValidationComplete);
        Assert.Equal(0, first.FilesReclaimed); // protected ordinal prefix
        platform.LastWatch!.SimulateOverflow();
        var next = await gc.CollectAsync(new(MaxNamespaceEntries: 1, Continuation: first.Continuation));
        Assert.True(next.ValidationRestarted);
        Assert.False(next.ValidationComplete);
        Assert.Equal(0, next.FilesReclaimed);
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventoryDeletedAndRecreatedDirectoryRestartsValidation()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        await f.Store.DeleteAsync(f.Id);
        AddSyntheticGeneration(f, new string('0', 32));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var first = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        Directory.Delete(f.Generations, recursive: true); // external namespace damage
        Directory.CreateDirectory(f.Generations, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        AddSyntheticGeneration(f, new string('f', 32));
        var next = await gc.CollectAsync(new(MaxNamespaceEntries: 1, Continuation: first.Continuation));
        Assert.True(next.ValidationRestarted);
        Assert.Equal(0, next.FilesReclaimed);
        Assert.Single(Directory.GetFiles(f.Generations));
    }

    [Fact]
    public async Task BoundedInventoryProcessDeathMakesSavedTokenUnusable()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-allowed"), f.Root);
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(ArtifactContentGarbageCollectionTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ArtifactContentGarbageCollectionTests.GarbageCollectionProcessWorker");
        info.Environment["EMF_GC_CHILD_ROOT"] = f.Root;
        info.Environment["EMF_GC_CHILD_POINT"] = "ContinuationReady";
        info.Environment["EMF_AZURE_OPENAI_LIVE"] = "false";
        info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false";
        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(Path.Combine(f.Root, "worker-ready")))
            {
                if (process.HasExited) throw new Exception(await output + await error);
                await Task.Delay(20, timeout.Token);
            }
            var token = await File.ReadAllTextAsync(Path.Combine(f.Root, "worker-token"));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
            await Assert.ThrowsAsync<ArgumentException>(() => gc.CollectAsync(new(Continuation: token)));
            Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
            Assert.Equal(1, (await gc.CollectAsync()).FilesReclaimed);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await output;
            await error;
        }
    }

    [Fact]
    public async Task BoundedInventoryWatcherFailureInvalidatesTokenAndPreventsDeletion()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        var platform = new NamespaceWatchPlatform();
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform);
        var first = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        platform.LastWatch!.Dispose();
        await Assert.ThrowsAsync<IOException>(() => gc.CollectAsync(new(Continuation: first.Continuation)));
        await Assert.ThrowsAsync<ArgumentException>(() => gc.CollectAsync(new(Continuation: first.Continuation)));
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventoryFreshCurrentStateBetweenReclamationCallsIsProtected()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('f', 32) + ".tmp");
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var first = await gc.CollectAsync(new(MaxEntriesSelected: 1));
        Assert.True(first.ValidationComplete);
        Assert.Equal(0, first.FilesReclaimed);
        await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        var current = await f.Store.ReadVersionedAsync(f.Id);
        var checks = 0;
        var exclusive = false;
        gc.Checkpoint = p =>
        {
            if (p == "ExclusiveGateAcquired") exclusive = true;
            if (p == "UnlinkProtectionRechecked") { Assert.True(exclusive); checks++; }
            return Task.CompletedTask;
        };
        var next = await gc.CollectAsync(new(Continuation: first.Continuation));
        Assert.True(next.ValidationRestarted);
        Assert.Equal(2, next.FilesReclaimed);
        Assert.Equal(next.FilesReclaimed, checks);
        Assert.Equal(current!.Revision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(new byte[] { 2 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(100001)]
    [InlineData(1000000)]
    [InlineData(int.MaxValue)]
    public async Task BoundedInventoryInvalidPerCallBudgetsRejectBeforeInspectionOrDeletion(int budget)
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        var platform = new NamespaceWatchPlatform();
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gc.CollectAsync(new(MaxNamespaceEntries: budget)));
        Assert.Equal(0, platform.WatchesCreated);
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
    }

    [Fact]
    public async Task BoundedInventoryMaximumPerCallBudgetIsAcceptedAndEqualsDefault()
    {
        Assert.Equal(100000, FileSystemArtifactContentGarbageCollector.MaximumNamespaceEntriesPerCall);
        Assert.Equal(100000, new ArtifactContentGarbageCollectionOptions().MaxNamespaceEntries);
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root);
        var result = await gc.CollectAsync(new(MaxNamespaceEntries: 100000));
        Assert.True(result.SweepComplete);
        Assert.Equal(2, result.EntriesInspected);
        Assert.Equal(1, result.FilesReclaimed);
    }

    [Fact]
    public async Task BoundedInventoryCollectorUsesPlatformWatchThroughoutContinuation()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        var platform = new NamespaceWatchPlatform();
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform);
        gc.Checkpoint = p =>
        {
            if (p == "NamespaceEntryValidated") Assert.NotNull(platform.LastWatch);
            return Task.CompletedTask;
        };
        var result = await gc.CollectAsync(new(MaxNamespaceEntries: 1));
        var watch = platform.LastWatch;
        Assert.NotNull(watch);
        Assert.Equal(f.Generations, platform.WatchedDirectory);
        Assert.Equal(1, platform.WatchesCreated);
        Assert.False(watch!.Disposed);
        Assert.True(watch.UnchangedChecks > 0);
        while (!result.SweepComplete)
            result = await gc.CollectAsync(new(MaxNamespaceEntries: 1, Continuation: result.Continuation));
        Assert.Same(watch, platform.LastWatch);
        Assert.Equal(1, platform.WatchesCreated);
        Assert.True(watch.ChangeChecks > 0);
        Assert.Equal(1, watch.DeletionAcknowledgements);
        Assert.True(watch.Disposed);
        Assert.Single(Directory.GetFiles(f.Generations));
    }

    [Fact]
    public async Task BoundedInventoryPlatformWatchCreationFailurePreventsReclamation()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        AddSyntheticGeneration(f, new string('0', 32));
        var platform = new NamespaceWatchPlatform { RejectWatchCreation = true };
        using var gc = new FileSystemArtifactContentGarbageCollector(f.Root, platform);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => gc.CollectAsync());
        Assert.Equal(1, platform.WatchesCreated);
        Assert.Equal(2, Directory.GetFiles(f.Generations).Length);
    }

    private sealed class NamespaceWatchPlatform : IContentStoragePlatform
    {
        private readonly IContentStoragePlatform _inner = ContentStoragePlatform.Select();
        public int WatchesCreated { get; private set; }
        public string? WatchedDirectory { get; private set; }
        public RecordingNamespaceWatch? LastWatch { get; private set; }
        public bool RejectWatchCreation { get; init; }
        public IGenerationNamespaceWatch CreateGenerationNamespaceWatch(string directory)
        {
            WatchesCreated++;
            WatchedDirectory = directory;
            if (RejectWatchCreation) throw new PlatformNotSupportedException("Synthetic unsupported namespace watch.");
            return LastWatch = new(_inner.CreateGenerationNamespaceWatch(directory));
        }
        public void RequirePlatform() => _inner.RequirePlatform();
        public void RequireSameFileSystem(string root, string parent) => _inner.RequireSameFileSystem(root, parent);
        public ContentSourceIdentity InspectSourceFile(string path) => _inner.InspectSourceFile(path);
        public FileStream OpenSourceFile(string path) => _inner.OpenSourceFile(path);
        public Task<IDisposable> AcquireAdmissionAsync(string root, CancellationToken ct) => _inner.AcquireAdmissionAsync(root, ct);
        public Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken ct) => _inner.AcquireAsync(path, exclusive, ct);
        public void FlushDirectory(string path, bool verifyFileSystem = false) => _inner.FlushDirectory(path, verifyFileSystem);
        public void CreatePrivateDirectory(string path) => _inner.CreatePrivateDirectory(path);
        public FileStream CreatePrivateFile(string path, bool asynchronous = false) => _inner.CreatePrivateFile(path, asynchronous);
        public void ValidatePrivatePermissions(string path) => _inner.ValidatePrivatePermissions(path);
    }

    private sealed class RecordingNamespaceWatch(IGenerationNamespaceWatch inner) : IGenerationNamespaceWatch
    {
        public int ChangeChecks { get; private set; }
        public int UnchangedChecks { get; private set; }
        public int DeletionAcknowledgements { get; private set; }
        public bool Disposed { get; private set; }
        public bool Changed() { ChangeChecks++; return inner.Changed(); }
        public void RequireUnchanged() { UnchangedChecks++; inner.RequireUnchanged(); }
        public void AcknowledgeOwnDeletion(string name) { DeletionAcknowledgements++; inner.AcknowledgeOwnDeletion(name); }
        public void SimulateOverflow() => ((LinuxGenerationNamespaceWatch)inner).SimulateOverflow = true;
        public void Dispose() { Disposed = true; inner.Dispose(); }
    }

}
