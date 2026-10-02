using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed class ArtifactContentMigrationPlatformTests
{
    [Fact]
    public async Task CrossFilesystemPreflightRejectsBeforeCreatingWorkspaceOrMovingSources()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "emf-migration-platform-" + Guid.NewGuid().ToString("N"));
        var platform = new RejectingStagingPlatform();
        platform.CreatePrivateDirectory(root);
        var source = Path.Combine(root, "synthetic");
        await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3 });
        try
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
                new FileSystemArtifactContentMigration(platform).MigrateAsync(root, new(true)));
            Assert.True(platform.Checked);
            Assert.Single(Directory.GetFileSystemEntries(root));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source));
            Assert.False(Directory.Exists(FileSystemArtifactContentMigration.WorkspaceFor(root)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LinuxAcceptsSharedMountAndRejectsDistinctMounts()
    {
        if (!OperatingSystem.IsLinux()) return;
        var platform = ContentStoragePlatform.Select();
        var root = Path.Combine(Path.GetTempPath(), "emf-migration-device-" + Guid.NewGuid().ToString("N"));
        platform.CreatePrivateDirectory(root);
        try
        {
            platform.RequireSameFileSystem(root, Path.GetDirectoryName(root)!);
            // Only directory identity is inspected; no system content is read.
            if (Directory.Exists("/proc/self"))
                Assert.Throws<PlatformNotSupportedException>(() => platform.RequireSameFileSystem("/proc", "/"));
        }
        finally { Directory.Delete(root); }
    }

    private sealed class RejectingStagingPlatform : IContentStoragePlatform
    {
        private readonly IContentStoragePlatform _inner = ContentStoragePlatform.Select();
        public bool Checked { get; private set; }
        public void RequirePlatform() => _inner.RequirePlatform();
        public void RequireSameFileSystem(string rootPath, string stagingParentPath)
        { Checked = true; throw new PlatformNotSupportedException("Synthetic distinct filesystem mounts."); }
        public void ValidatePrivatePermissions(string path) => _inner.ValidatePrivatePermissions(path);
        public void CreatePrivateDirectory(string path) => _inner.CreatePrivateDirectory(path);
        public ContentSourceIdentity InspectSourceFile(string path) => throw new InvalidOperationException();
        public FileStream OpenSourceFile(string path) => throw new InvalidOperationException();
        public Task<IDisposable> AcquireAdmissionAsync(string rootPath, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public void FlushDirectory(string path, bool verifyFileSystem = false) => throw new InvalidOperationException();
        public FileStream CreatePrivateFile(string path, bool asynchronous = false) => throw new InvalidOperationException();
    }
}
