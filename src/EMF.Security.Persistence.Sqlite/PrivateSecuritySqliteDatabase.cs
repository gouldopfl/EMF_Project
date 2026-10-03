using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace EMF.Security.Persistence.Sqlite;

internal static class PrivateSecuritySqliteDatabase
{
    internal static void Prepare(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Private security persistence requires Linux.");
        var parent = Path.GetDirectoryName(path)!;
        for (var ancestor = parent; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (new DirectoryInfo(ancestor).LinkTarget is not null) throw new IOException("Security persistence path contains a symbolic link.");
        Directory.CreateDirectory(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((File.GetUnixFileMode(parent) & forbidden) != 0) throw new IOException("Security persistence directory must be private.");
        if (!File.Exists(path))
        {
            using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }); stream.Flush(true);
        }
        if (new FileInfo(path).LinkTarget is not null || (File.GetUnixFileMode(path) & forbidden) != 0)
            throw new IOException("Security database must be private.");
        FlushDirectory(parent); FlushDirectory(Path.GetDirectoryName(parent)!);
    }
    private static void FlushDirectory(string path)
    {
        var fd = open(path, 0x10000 | 0x20000 | 0x80000, 0);
        if (fd < 0) throw new IOException("Security persistence directory cannot be opened for synchronization.");
        using var handle = new SafeFileHandle((IntPtr)fd, true);
        if (fsync(fd) != 0) throw new IOException("Security persistence directory synchronization failed.");
    }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags, int mode);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
}
