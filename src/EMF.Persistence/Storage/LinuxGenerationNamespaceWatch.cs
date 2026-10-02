using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EMF.Persistence.Storage;

// Synchronous kernel events, not FileSystemWatcher's asynchronous callbacks.
// Any event (including queue overflow / lost watch) invalidates validation. Reads
// are bounded and fail closed; no attempt is made to reconstruct a lost epoch.
internal sealed class LinuxGenerationNamespaceWatch : IGenerationNamespaceWatch
{
    [DllImport("libc", SetLastError = true)] private static extern int inotify_init1(int flags);
    [DllImport("libc", SetLastError = true)] private static extern int inotify_add_watch(int fd, string path, uint mask);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte[] buffer, nuint count);
    private readonly SafeFileHandle _handle;
    private readonly byte[] _buffer = new byte[4096];
    public LinuxGenerationNamespaceWatch(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("GC namespace continuation requires Linux inotify.");
        var fd = inotify_init1(0x800 | 0x80000); // NONBLOCK | CLOEXEC
        if (fd < 0) throw new IOException("Cannot establish namespace change watch.");
        _handle = new((IntPtr)fd, true);
        // MODIFY, ATTRIB, MOVED_FROM/TO, CREATE, DELETE, DELETE_SELF, MOVE_SELF;
        // ONLYDIR | DONT_FOLLOW. Access/open/close events are deliberately omitted.
        if (inotify_add_watch(fd, path, 0x2 | 0x4 | 0x40 | 0x80 | 0x100 | 0x200 | 0x400 | 0x800 | 0x1000000 | 0x2000000) < 0)
        { _handle.Dispose(); throw new IOException("Cannot watch generation namespace."); }
    }
    internal bool SimulateOverflow { get; set; }
    private int ReadEvents()
    {
        if (_handle.IsClosed || _handle.IsInvalid) throw new IOException("Namespace watch handle lost; restart validation.");
        if (SimulateOverflow)
        {
            Array.Clear(_buffer);
            BitConverter.GetBytes(0x4000u).CopyTo(_buffer, 4); // IN_Q_OVERFLOW
            return 16;
        }
        var count = read(_handle.DangerousGetHandle().ToInt32(), _buffer, (nuint)_buffer.Length);
        if (count > 0) return checked((int)count);
        if (count == 0) throw new IOException("Namespace watch lost event coverage.");
        if (Marshal.GetLastPInvokeError() == 11) return 0; // EAGAIN
        throw new IOException("Cannot verify namespace change watch.");
    }
    public bool Changed() => ReadEvents() != 0;
    public void RequireUnchanged()
    {
        if (Changed()) throw new IOException("Generation namespace changed; restart validation without continuation.");
    }
    public void AcknowledgeOwnDeletion(string name)
    {
        var count = ReadEvents();
        // Exactly one IN_DELETE for this unlink; unexpected/coalesced/overflow
        // events never get silently drained as if they were collector writes.
        if (count < 16 || BitConverter.ToUInt32(_buffer, 4) != 0x200 ||
            16 + BitConverter.ToUInt32(_buffer, 12) != count ||
            Encoding.UTF8.GetString(_buffer, 16, count - 16).TrimEnd('\0') != name)
            throw new IOException("Unexpected namespace mutation during reclamation.");
        RequireUnchanged();
    }
    public void Dispose() => _handle.Dispose();
}
