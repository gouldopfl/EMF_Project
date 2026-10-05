using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace EMF.Persistence.Storage;

// Linux-only infrastructure. Local ext-family, XFS and Btrfs filesystems are
// admitted; network/overlay/tmpfs roots cannot advertise durable receipts.
[SupportedOSPlatform("linux")]
internal sealed class LinuxContentDurability : IContentStoragePlatform
{
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int flock(int fd, int operation);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int fstatfs(int fd, IntPtr buffer);
    [DllImport("libc", SetLastError = true)] private static extern int statx(int dirfd, string path, int flags, uint mask, IntPtr buffer);
    [DllImport("libc")] private static extern uint geteuid();
    [StructLayout(LayoutKind.Sequential)]
    private struct InspectionTimespec { public long Seconds; public long Nanoseconds; }
    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clockId, out InspectionTimespec time);
    internal static TimeSpan InspectionProcessingTime()
    {
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Bounded content inspection requires supported 64-bit Linux.");
        // CLOCK_THREAD_CPUTIME_ID: unrelated threads, scheduling, SQLite busy waits,
        // filesystem wait and physical payload I/O cannot spend this operation's work.
        if (clock_gettime(3, out var time) != 0) throw new IOException("Cannot measure bounded inspection work.");
        return TimeSpan.FromTicks(checked(time.Seconds * TimeSpan.TicksPerSecond + time.Nanoseconds / 100));
    }
    public IGenerationNamespaceWatch CreateGenerationNamespaceWatch(string directory)
    {
        RequirePlatform();
        return new LinuxGenerationNamespaceWatch(directory);
    }
    public void RequirePlatform()
    {
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Versioned content requires supported 64-bit Linux local storage.");
    }
    public void RequireSameFileSystem(string rootPath, string stagingParentPath)
    {
        RequirePlatform();
        if (DirectoryIdentity(rootPath) != DirectoryIdentity(stagingParentPath))
            throw new PlatformNotSupportedException("Offline content migration requires the root and its staging parent on the same filesystem mount.");
    }

    private static (uint Major, uint Minor, ulong Mount) DirectoryIdentity(string path)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            // Device identity alone misses bind-mount boundaries: rename can return
            // EXDEV even when both paths belong to the same underlying filesystem.
            const uint requested = 0x1001; // STATX_TYPE | STATX_MNT_ID
            if (statx(-100, path, 0x100, requested, buffer) != 0)
                throw new IOException("Cannot inspect migration filesystem identity.");
            if ((unchecked((uint)Marshal.ReadInt32(buffer)) & requested) != requested)
                throw new PlatformNotSupportedException("Migration filesystem mount identity is unavailable.");
            if (((ushort)Marshal.ReadInt16(buffer, 28) & 0xF000) != 0x4000)
                throw new IOException("Migration root and staging parent must be directories.");
            return (unchecked((uint)Marshal.ReadInt32(buffer, 136)), unchecked((uint)Marshal.ReadInt32(buffer, 140)),
                unchecked((ulong)Marshal.ReadInt64(buffer, 144)));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private SafeFileHandle OpenGate(string path)
    {
        RequirePlatform();
        if (File.Exists(path)) ValidatePrivatePermissions(path);
        var fd = open(path, 2 | 64 | 0x20000 | 0x80000, 0x180); // RDWR|CREAT|NOFOLLOW|CLOEXEC, 0600
        if (fd < 0) throw new IOException("Cannot open stable content coordination.");
        return new SafeFileHandle((IntPtr)fd, true);
    }
    public async Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken ct)
    {
        return await AcquireHandleAsync(OpenGate(path), exclusive, ct);
    }
    public Task<IDisposable> AcquireAdmissionAsync(string rootPath, CancellationToken ct)
    {
        RequirePlatform();
        var fd = open(rootPath, 0x10000 | 0x20000 | 0x80000, 0); // Read-only DIRECTORY|NOFOLLOW|CLOEXEC
        if (fd < 0) throw new IOException("Cannot open content root admission coordination.");
        return AcquireHandleAsync(new SafeFileHandle((IntPtr)fd, true), true, ct);
    }
    private static async Task<IDisposable> AcquireHandleAsync(SafeFileHandle handle, bool exclusive, CancellationToken ct)
    {
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (flock(handle.DangerousGetHandle().ToInt32(), (exclusive ? 2 : 1) | 4) == 0) return handle;
                var error = Marshal.GetLastPInvokeError();
                if (error != 11 && error != 4) throw new IOException("Content coordination failed.");
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(10)) throw new TimeoutException("Content coordination timed out.");
                await Task.Delay(10, ct);
            }
        }
        catch { handle.Dispose(); throw; }
    }
    public void CreatePrivateDirectory(string path)
        => Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    public FileStream CreatePrivateFile(string path, bool asynchronous = false)
        => new(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = asynchronous ? FileOptions.Asynchronous : FileOptions.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });

    public void ValidatePrivatePermissions(string path)
    {
        RequirePlatform();
        // statx has a fixed Linux ABI, unlike architecture-dependent struct stat.
        // AT_SYMLINK_NOFOLLOW prevents permission checks from accepting a link target.
        var buffer = Marshal.AllocHGlobal(256);
        UnixFileMode mode;
        bool directory;
        try
        {
            if (statx(-100, path, 0x100, 0xA, buffer) != 0)
            {
                if (Marshal.GetLastPInvokeError() == 2) throw new FileNotFoundException("Content path disappeared.");
                throw new IOException("Cannot validate content path ownership and permissions.");
            }
            if ((Marshal.ReadInt32(buffer) & 0xA) != 0xA)
                throw new PlatformNotSupportedException("Content path ownership and permissions are unavailable.");
            var rawMode = (ushort)Marshal.ReadInt16(buffer, 28);
            directory = (rawMode & 0xF000) == 0x4000;
            if (!directory && (rawMode & 0xF000) != 0x8000)
                throw new IOException("Content protocol paths must be regular files or directories.");
            if (unchecked((uint)Marshal.ReadInt32(buffer, 20)) != geteuid())
                throw new IOException("Content path ownership is unsafe; the effective store user must own it.");
            mode = (UnixFileMode)(rawMode & 0xFFF);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        const UnixFileMode unauthorized = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute |
            UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;
        var required = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (directory) required |= UnixFileMode.UserExecute;
        if ((mode & unauthorized) != 0 || (mode & required) != required)
            throw new IOException("Content storage permissions are unsafe; owner-only access is required.");
    }

    public ContentSourceIdentity InspectSourceFile(string path)
    {
        RequirePlatform();
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (statx(-100, path, 0x100, 0x7FF, buffer) != 0)
                throw new IOException("Cannot inspect retained legacy source.");
            if ((Marshal.ReadInt32(buffer) & 0x7FF) != 0x7FF ||
                ((ushort)Marshal.ReadInt16(buffer, 28) & 0xF000) != 0x8000 ||
                Marshal.ReadInt32(buffer, 16) != 1 || unchecked((uint)Marshal.ReadInt32(buffer, 20)) != geteuid())
                throw new IOException("Legacy sources must be owned regular files without links.");
            // Rename changes ctime. Identity, size, mode and mtime are retained;
            // exact bytes are independently rechecked against the private digest.
            var stamp = string.Join(":", new long[] { Marshal.ReadInt32(buffer, 136), Marshal.ReadInt32(buffer, 140),
                Marshal.ReadInt64(buffer, 32), Marshal.ReadInt16(buffer, 28), Marshal.ReadInt32(buffer, 20),
                Marshal.ReadInt32(buffer, 24), Marshal.ReadInt64(buffer, 40), Marshal.ReadInt64(buffer, 112), Marshal.ReadInt32(buffer, 120) });
            return new(Marshal.ReadInt64(buffer, 40), stamp);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public FileStream OpenSourceFile(string path)
    {
        RequirePlatform();
        // NOATIME avoids altering retained evidence metadata while inspecting it.
        var fd = open(path, 0x20000 | 0x80000 | 0x40000, 0); // RDONLY|NOFOLLOW|CLOEXEC|NOATIME
        if (fd < 0) throw new IOException("Cannot open retained legacy source without changing access metadata.");
        return new FileStream(new SafeFileHandle((IntPtr)fd, true), FileAccess.Read);
    }

    public void FlushDirectory(string path, bool verifyFileSystem = false)
    {
        RequirePlatform();
        var fd = open(path, 0x10000 | 0x20000 | 0x80000, 0); // DIRECTORY|NOFOLLOW|CLOEXEC
        if (fd < 0) throw new IOException("Cannot open content directory durability barrier.");
        using var handle = new SafeFileHandle((IntPtr)fd, true);
        if (verifyFileSystem)
        {
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                if (fstatfs(fd, buffer) != 0) throw new IOException("Cannot validate content filesystem.");
                var type = Marshal.ReadInt64(buffer);
                if (type != 0xEF53 && type != 0x58465342 && type != 0x9123683E)
                    throw new PlatformNotSupportedException("Content filesystem durability is not supported.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        if (fsync(fd) != 0) throw new IOException("Content directory durability barrier failed.");
    }
}
